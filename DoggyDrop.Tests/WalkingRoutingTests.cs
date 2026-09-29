using System.Net;
using System.Text;
using System.Text.Json;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkingRoutingTests
{
    private static readonly WalkingCoordinate[] Points = [new(46.56, 15.64), new(46.561, 15.641)];
    public const string ValidJson = """{"features":[{"geometry":{"type":"LineString","coordinates":[[15.64,46.56],[15.6405,46.5605],[15.641,46.561]]},"properties":{"summary":{"distance":245.5,"duration":189}}}]}""";
    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Calls++; return send(request, ct); }
    }
    private static HttpResponseMessage Response(string json = ValidJson, HttpStatusCode status = HttpStatusCode.OK) => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    private static OrsWalkingRoutes Service(Handler handler, string? key = "test-only-key", WalkingRouteBudget? budget = null) =>
        new(new HttpClient(handler), new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["OpenRouteService:ApiKey"] = key }).Build(),
            budget ?? new(TimeProvider.System), NullLogger<OrsWalkingRoutes>.Instance);

    [Fact]
    public async Task FootWalkingPostUsesOnlyCoordinatesAndServerAuthorizationAndProviderMetrics()
    {
        var handler = new Handler(async (r, ct) => {
            Assert.Equal(HttpMethod.Post, r.Method); Assert.Equal("https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson", r.RequestUri!.AbsoluteUri);
            Assert.Equal("test-only-key", r.Headers.GetValues("Authorization").Single());
            var text = await r.Content!.ReadAsStringAsync(ct); using var body = JsonDocument.Parse(text);
            Assert.Equal(new[] { "coordinates", "preference", "instructions", "elevation" }, body.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal(15.64, body.RootElement.GetProperty("coordinates")[0][0].GetDouble());
            Assert.DoesNotContain("test-only-key", text); Assert.DoesNotContain("UserId", text);
            return Response();
        });
        var result = await Service(handler).RouteAsync(Points);
        Assert.True(result.IsRouted); Assert.Equal(245.5, result.DistanceMeters); Assert.Equal(189, result.DurationSeconds);
        Assert.Equal(new WalkingCoordinate(46.5605, 15.6405), result.Points[1]); Assert.Equal(1, handler.Calls);
    }
    [Theory] [InlineData(null)] [InlineData("")] [InlineData(" ")]
    public async Task MissingKeyNeverCallsProvider(string? key)
    {
        var handler = new Handler((_, _) => throw new Exception("network must not be called"));
        Assert.False((await Service(handler, key).RouteAsync(Points)).IsRouted); Assert.Equal(0, handler.Calls);
    }
    [Theory] [InlineData(401)] [InlineData(403)] [InlineData(404)] [InlineData(500)] [InlineData(503)]
    public async Task ErrorsNeverTryAnotherProvider(int status)
    {
        var handler = new Handler((_, _) => Task.FromResult(Response(status: (HttpStatusCode)status)));
        var result = await Service(handler).RouteAsync(Points); Assert.False(result.IsRouted); Assert.Empty(result.Points); Assert.Equal(1, handler.Calls);
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"features\":[]}")]
    [InlineData("not json")]
    [InlineData("{\"features\":[{\"geometry\":{\"type\":\"Point\",\"coordinates\":[1,2]}}]}")]
    [InlineData("{\"features\":[{\"geometry\":{\"type\":\"LineString\",\"coordinates\":[[],[]]}}]}")]
    public async Task MalformedProviderResponseFailsClosed(string json)
    {
        Assert.False((await Service(new Handler((_, _) => Task.FromResult(Response(json)))).RouteAsync(Points)).IsRouted);
    }
    [Theory] [InlineData("missing")] [InlineData("negative")] [InlineData("large")] [InlineData("null")]
    public async Task UnavailableDurationIsNotInvented(string kind)
    {
        var json = ValidJson.Replace(",\"duration\":189", kind == "missing" ? "" : kind == "null" ? ",\"duration\":null" : kind == "negative" ? ",\"duration\":-1" : ",\"duration\":1e300");
        var result = await Service(new Handler((_, _) => Task.FromResult(Response(json)))).RouteAsync(Points);
        Assert.True(result.IsRouted); Assert.Null(result.DurationSeconds);
    }
    [Fact]
    public async Task InvalidCoordinateOrOversizedTripNeverCallsProvider()
    {
        var handler = new Handler((_, _) => throw new Exception("network")); var service = Service(handler);
        foreach (var invalid in new WalkingCoordinate[][] { [], [Points[0]], [Points[0], Points[0]], [Points[0], new(double.NaN, 15)], [Points[0], new(91, 15)], [Points[0], new(-46, -100)], Enumerable.Repeat(Points, 7).SelectMany(x => x).ToArray() })
            Assert.Equal("invalid", (await service.RouteAsync(invalid)).Failure);
        Assert.Equal(0, handler.Calls);
    }
    [Fact]
    public async Task CancellationPropagatesAndTransportFailureIsUnavailable()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var cancelled = Service(new Handler((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled.RouteAsync(Points, cts.Token));
        Assert.False((await Service(new Handler((_, _) => throw new HttpRequestException("private provider body"))).RouteAsync(Points)).IsRouted);
        Assert.False((await Service(new Handler((_, _) => throw new TaskCanceledException())).RouteAsync(Points)).IsRouted);
    }
    [Fact]
    public async Task OversizedProviderBodyIsRejected()
    {
        Assert.False((await Service(new Handler((_, _) => Task.FromResult(Response(new string('x', 2_000_001))))).RouteAsync(Points)).IsRouted);
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = new(2026, 9, 28, 0, 0, 0, TimeSpan.Zero); public override DateTimeOffset GetUtcNow() => Now; }
    [Fact]
    public void BudgetCapsMinuteAndDayAndRecoversWithoutRetainingRoutes()
    {
        var clock = new Clock(); var budget = new WalkingRouteBudget(clock);
        for (var minute = 0; minute < 60; minute++)
        {
            for (var n = 0; n < 30; n++) Assert.True(budget.TryTake(out _));
            Assert.False(budget.TryTake(out var wait)); Assert.True(wait > 0); clock.Now = clock.Now.AddMinutes(1);
        }
        Assert.False(budget.TryTake(out _)); clock.Now = clock.Now.AddDays(1); Assert.True(budget.TryTake(out _));
    }
    [Fact]
    public async Task Provider429SetsSharedCooldownWithoutAutomaticRetries()
    {
        var budget = new WalkingRouteBudget(TimeProvider.System);
        var handler = new Handler((_, _) => { var r = Response(status: HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromSeconds(120)); return Task.FromResult(r); });
        var result = await Service(handler, budget: budget).RouteAsync(Points); Assert.Equal(120, result.RetryAfterSeconds);
        Assert.Equal("busy", (await Service(handler, budget: budget).RouteAsync(Points)).Failure); Assert.Equal(1, handler.Calls);
    }
    private sealed class FakeRoutes(bool available) : IWalkingRoutes
    {
        public IReadOnlyList<WalkingCoordinate>? Received;
        public Task<WalkingRouteResult> RouteAsync(IReadOnlyList<WalkingCoordinate> points, CancellationToken ct = default)
        { Received = points; return Task.FromResult(available ? new WalkingRouteResult(Points, 245.5, 189) : WalkingRouteResult.Unavailable("unavailable")); }
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task PlannerKeepsStopOrderAndDistinguishesGeneratedGeometry(bool available)
    {
        var routes = new FakeRoutes(available);
        var plan = new PlannedWalkRoute { EstimatedDistanceKm = 0.2, EstimatedMinutes = 99, Summary = "Plan.", RoutePoints = [new() { Latitude = 1, Longitude = 2 }],
            Stops = [new() { Order = 2, Latitude = 46.561, Longitude = 15.641 }, new() { Order = 1, Latitude = 46.56, Longitude = 15.64 }] };
        await WalkingPlanRouting.ApplyAsync(plan, routes, default);
        Assert.Equal(Points, routes.Received); Assert.Equal(available, plan.IsWalkingRoute);
        Assert.Equal(available ? 4 : 0, plan.EstimatedMinutes);
        Assert.Equal(available ? 0.2455 : 0.2, plan.EstimatedDistanceKm);
        Assert.Contains(available ? "OpenRouteService" : "niso preverjena pešpot", plan.Summary);
    }
    [Fact]
    public async Task OsmStopDiscoveryDelegatesOnlyToSharedWalkingService()
    {
        var handler = new Handler((r, _) => { Assert.Equal("overpass-api.de", r.RequestUri!.Host); return Task.FromResult(Response("{\"elements\":[]}")); });
        var routes = new FakeRoutes(true);
        var planner = new OsmWalkPlannerService(new HttpClient(handler), NullLogger<OsmWalkPlannerService>.Instance, routes);
        var plan = await planner.PlanAsync(46.56, 15.64, 3, [new TrashBin { Latitude = 46.561, Longitude = 15.641, Name = "Bin" }], "quick", "auto", true, false, false, false);
        Assert.NotNull(plan); Assert.True(plan.IsWalkingRoute); Assert.Equal(4, plan.EstimatedMinutes);
        Assert.Equal(3, routes.Received!.Count); Assert.Equal(routes.Received[0], routes.Received[^1]); Assert.Equal(1, handler.Calls);
    }
}
