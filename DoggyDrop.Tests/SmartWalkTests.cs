using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class SmartWalkTests
{
    internal static readonly WalkingCoordinate Start = new(46, 15);
    internal static SmartWalkInput Input(int minutes = 30) => new(Start, minutes, "loop", true, true, true);
    internal static WalkingRouteResult Loop(int minutes = 30) => new(SmartWalkGeometry.Candidates(Input(minutes), [])[0].Anchors, minutes * 75, minutes * 60);

    [Theory]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    public void BoundedDeterministicNonDegenerateCandidates(int minutes)
    {
        var input = Input(minutes); var a = SmartWalkGeometry.Candidates(input, []); var b = SmartWalkGeometry.Candidates(input, []);
        Assert.Equal(3, a.Count);
        for (var i = 0; i < a.Count; i++) { Assert.Equal(a[i].Anchors, b[i].Anchors); Assert.Equal(Start, a[i].Anchors[0]); Assert.Equal(Start, a[i].Anchors[^1]); Assert.Equal(3, a[i].Anchors.Distinct().Count()); }
        Assert.NotEqual(a[0].Anchors[1], SmartWalkGeometry.Candidates(input with { Variant = 1 }, [])[0].Anchors[1]);
    }
    [Theory]
    [InlineData("water", 25)]
    [InlineData("bin", 25)]
    [InlineData("park", 40)]
    public void CorridorUsesSegmentsAndInclusiveBoundaryNotOnlyVertices(string kind, double threshold)
    {
        WalkingCoordinate[] route = [new(0, 0), new(0, .01)];
        SmartWalkPoi Poi(int id, double offset) => new(id, kind, "Public", offset / 6371000 * 180 / Math.PI, .005);
        var facts = SmartWalkGeometry.Facts(route, [Poi(1, threshold - .01), Poi(2, threshold), Poi(3, threshold + .01)]);
        Assert.Equal(new[] { 1, 2 }, facts.Select(p => p.Id));
    }
    [Fact]
    public void MissingInfrastructureDoesNotInventFactsOrBonuses()
    {
        var route = Loop(); Assert.Empty(SmartWalkGeometry.Facts(route.Points, [])); Assert.Equal(0, SmartWalkGeometry.Score(Input(), route, []).InfrastructureBonus);
        Assert.Empty(SmartWalkGeometry.Facts(route.Points, [new(1, "water", "Far away", 47, 16)]));
    }
    [Fact]
    public void RepeatedEdgesArePenalizedIncludingReverseDirection()
    {
        var loop = Loop(); var outAndBack = new[] { Start, SmartWalkGeometry.Offset(Start, 700, 0), Start, SmartWalkGeometry.Offset(Start, 700, 0), Start };
        Assert.True(SmartWalkGeometry.Repetition(outAndBack) > .5);
        Assert.True(SmartWalkGeometry.Repetition(loop.Points) < .1);
    }
    [Fact]
    public void PreferenceBonusesAreCappedAndDoNotMakeLongDetourValid()
    {
        var input = Input(); var candidate = SmartWalkGeometry.Candidates(input, [])[0]; var ideal = Loop();
        var water = new SmartWalkPoi(1, "water", "Pitnik", 46, 15);
        Assert.True(SmartWalkGeometry.Score(input, ideal with { DurationSeconds = 1980 }, [water]).Total > SmartWalkGeometry.Score(input, ideal, []).Total);
        Assert.False(SmartWalkGeometry.ValidRoute(ideal with { DurationSeconds = 3300 }, input, candidate));
        Assert.Equal(SmartWalkGeometry.Score(input, ideal, [water]).InfrastructureBonus, SmartWalkGeometry.Score(input, ideal, Enumerable.Repeat(water, 100).ToArray()).InfrastructureBonus);
        var bins = Enumerable.Range(1, 100).Select(i => new SmartWalkPoi(i, "bin", "Koš", 46, 15)).ToArray();
        Assert.Equal(SmartWalkPolicy.BinBonus, SmartWalkGeometry.Score(input, ideal, bins).InfrastructureBonus);
    }
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(90)]
    public void UnsupportedDurationRejected(int minutes) => Assert.False(Input(minutes).IsValid);
    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(90, 180, true)]
    [InlineData(91, 15, false)]
    [InlineData(46, 181, false)]
    public void CoordinatePolicyRetainsExplicitZero(double lat, double lon, bool valid) => Assert.Equal(valid, (Input() with { Start = new(lat, lon) }).IsValid);
    [Fact]
    public async Task NearbyUsesCurrentPublicRowsAndBoundedProjections()
    {
        await using var f = await SmartFixture.Create();
        f.Db.WaterPoints.AddRange(new() { Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking }, new() { Latitude = 46, Longitude = 15, IsApproved = false, Potability = WaterPotability.SourceReportedDrinking }, new() { Latitude = 46, Longitude = 15, IsApproved = true, IsRetired = true, Potability = WaterPotability.SourceReportedDrinking }, new() { Latitude = 46, Longitude = 15, IsApproved = false, Potability = WaterPotability.NotDrinking }, new() { Latitude = 46, Longitude = 15, IsApproved = false, Potability = WaterPotability.SourceReportedDrinking, Access = WaterAccess.Restricted });
        f.Db.Places.AddRange(new() { Name = "Park", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15 }, new() { Name = "Shop", Category = PlaceCategory.PetShop, Latitude = 46, Longitude = 15 }, new() { Name = "Inactive", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15, IsActive = false });
        f.Db.TrashBins.AddRange(new() { Name = "Public", Latitude = 46, Longitude = 15, IsApproved = true }, new() { Name = "Pending", Latitude = 46, Longitude = 15 }, new() { Name = "Retired", Latitude = 46, Longitude = 15, IsApproved = true, IsRetired = true });
        await f.Db.SaveChangesAsync(); var rows = await new SmartWalkPlanner(f.Db, new FakeRoutes()).LoadNearbyAsync(Input(), default);
        Assert.Equal(new[] { "bin", "water", "park" }, rows.Select(p => p.Kind)); Assert.Equal("Pitnik", rows[1].Name);
    }
    [Theory]
    [InlineData(250)]
    [InlineData(500)]
    [InlineData(1500)]
    [InlineData(3000)]
    public async Task LargeInfrastructureDatasetRemainsBounded(int count)
    {
        await using var f = await SmartFixture.Create();
        f.Db.WaterPoints.AddRange(Enumerable.Range(0, count).Select(i => new WaterPoint { Latitude = 46 + i * .000001, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking })); await f.Db.SaveChangesAsync();
        var rows = await new SmartWalkPlanner(f.Db, new FakeRoutes()).LoadNearbyAsync(Input(), default); Assert.InRange(rows.Count, 1, 120);
        Assert.Equal(rows.Count, rows.DistinctBy(p => (p.Kind, p.Id)).Count());
        var points = Enumerable.Range(0, 20000).Select(i => new WalkingCoordinate(46 + i * .0000001, 15)).ToArray();
        Assert.InRange(SmartWalkGeometry.Facts(points, rows).Count, 1, 120);
    }
    [Fact]
    public async Task ProviderCapAndBestCandidateSurviveLaterBudgetFailure()
    {
        await using var f = await SmartFixture.Create(); var routes = new FakeRoutes { Handler = (n, p) => n == 1 ? new(p, 2250, 1800) : WalkingRouteResult.Unavailable("busy") };
        var result = await new SmartWalkPlanner(f.Db, routes).GenerateAsync(Input(), default); Assert.NotNull(result); Assert.Equal(2, routes.Calls); Assert.Equal(2, result.Attempts); Assert.Equal(1800, result.Route.DurationSeconds);
    }
    [Fact]
    public async Task AllFailuresStopAtThreeAndNeverSaveApproximateGeometry()
    {
        await using var f = await SmartFixture.Create(); var routes = new FakeRoutes { Handler = (_, _) => WalkingRouteResult.Unavailable("unavailable") };
        Assert.Null(await new SmartWalkPlanner(f.Db, routes).GenerateAsync(Input(), default)); Assert.Equal(3, routes.Calls); Assert.Empty(await f.Db.PlannedWalks.ToListAsync());
    }
    [Fact]
    public async Task InvalidInputUsesNoProviderAndCancellationPropagates()
    {
        await using var f = await SmartFixture.Create(); var routes = new FakeRoutes(); var planner = new SmartWalkPlanner(f.Db, routes);
        Assert.Null(await planner.GenerateAsync(Input(-1), default)); Assert.Equal(0, routes.Calls);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planner.GenerateAsync(Input(), new CancellationToken(true)));
    }
    [Fact]
    public async Task ExplicitDestinationUsesActualPlaceAndInactiveDestinationUsesNoProvider()
    {
        await using var f = await SmartFixture.Create(); var place = new Place { Name = "Veterinar", Category = PlaceCategory.Veterinarian, Latitude = 46.005, Longitude = 15 }; f.Db.Places.Add(place); await f.Db.SaveChangesAsync();
        var routes = new FakeRoutes(); var planner = new SmartWalkPlanner(f.Db, routes); var request = Input() with { WalkType = "destination", PlaceId = place.Id };
        var selected = await planner.GenerateAsync(request, default); Assert.NotNull(selected); Assert.Single(selected.Stops); Assert.Equal(place.Id, selected.Stops[0].Id); Assert.Equal(1, routes.Calls);
        place.IsActive = false; await f.Db.SaveChangesAsync(); Assert.Null(await planner.GenerateAsync(request, default)); Assert.Equal(1, routes.Calls);
    }
    [Fact]
    public async Task PreviewAndSavedStopRetirementReactivationAndMoveRevalidateWithoutRewritingHistory()
    {
        await using var f = await SmartFixture.Create(); var water = new WaterPoint { Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking }; f.Db.WaterPoints.Add(water); await f.Db.SaveChangesAsync();
        var stop = new SmartWalkPoi(water.Id, "water", "Pitnik", 46, 15); var selection = new SmartWalkSelection(Input(), Loop(), [stop], [stop], [], 1); var plan = SmartWalkEligibility.ToPlan("owner", null, selection);
        Assert.Equal(Loop().Points.Select(p => p.Latitude), plan.RoutePoints!.Select(p => p.Latitude)); Assert.Equal(SmartWalkPolicy.Version, plan.AreaKey);
        Assert.True(await SmartWalkEligibility.PreviewAvailableAsync(f.Db, [stop], default));
        water.IsRetired = true; await f.Db.SaveChangesAsync(); Assert.False(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default)); Assert.False(await SmartWalkEligibility.PreviewAvailableAsync(f.Db, [stop], default));
        water.IsRetired = false; await f.Db.SaveChangesAsync(); Assert.True(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default));
        water.Latitude = 47; await f.Db.SaveChangesAsync(); Assert.False(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default)); Assert.Equal(46, plan.Stops!.Single(s => s.Type == "water").Latitude);
    }
    [Fact]
    public void PreviewTokensAreOwnerBoundReplacedAndRateLimited()
    {
        using var previews = new SmartWalkPreviews(TimeProvider.System); var selection = new SmartWalkSelection(Input(), Loop(), [], [], [], 1);
        var first = previews.Put("owner", selection); Assert.Null(previews.Get("other", first.Token)); Assert.NotNull(previews.Get("owner", first.Token)); previews.Put("owner", selection); Assert.Null(previews.Get("owner", first.Token));
        Assert.True(previews.TryBegin("owner", out var lease)); Assert.False(previews.TryBegin("owner", out _)); lease!.Dispose();
        for (var i = 0; i < 2; i++) { Assert.True(previews.TryBegin("owner", out lease)); lease!.Dispose(); }
        Assert.False(previews.TryBegin("owner", out _));
    }
    internal sealed class FakeRoutes : IWalkingRoutes
    {
        public int Calls; public Func<int, IReadOnlyList<WalkingCoordinate>, WalkingRouteResult> Handler = (_, p) => new(p, 2250, 1800);
        public Task<WalkingRouteResult> RouteAsync(IReadOnlyList<WalkingCoordinate> points, CancellationToken ct = default) { ct.ThrowIfCancellationRequested(); return Task.FromResult(Handler(++Calls, points)); }
    }
    [Fact]
    public void ExpiryAndPerOwnerWindowUseClockAndReleaseLeases()
    {
        var clock = new Clock(); using var previews = new SmartWalkPreviews(clock); var selection = new SmartWalkSelection(Input(), Loop(), [], [], [], 1);
        var preview = previews.Put("owner", selection); clock.Now = clock.Now.AddMinutes(10); Assert.Null(previews.Get("owner", preview.Token));
        var leases = new List<IDisposable>(); for (var i = 0; i < 8; i++) { Assert.True(previews.TryBegin("owner" + i, out var lease)); leases.Add(lease!); }
        Assert.False(previews.TryBegin("ninth", out _));
        leases.ForEach(l => l.Dispose()); Assert.True(previews.TryBegin("ninth", out var free)); free!.Dispose();
    }
    [Fact]
    public async Task AValidCandidateSurvivesLaterInternalTimeout()
    {
        await using var f = await SmartFixture.Create(); var routes = new FakeRoutes { Handler = (n, p) => n == 1 ? new(p, 2250, 1800) : throw new OperationCanceledException() };
        var selected = await new SmartWalkPlanner(f.Db, routes).GenerateAsync(Input(), default); Assert.NotNull(selected); Assert.Equal(2, routes.Calls);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidProviderMetricsAreNotCandidates(double distance)
    {
        Assert.False(SmartWalkGeometry.ValidRoute(Loop() with { DistanceMeters = distance }, Input(), SmartWalkGeometry.Candidates(Input(), [])[0]));
    }
    [Fact]
    public void DegenerateAndUnclosedLoopsCannotBePresentedAsWalkingSuggestions()
    {
        var candidate = SmartWalkGeometry.Candidates(Input(), [])[0]; Assert.False(SmartWalkGeometry.ValidRoute(Loop() with { Points = [Start, Start] }, Input(), candidate));
        Assert.False(SmartWalkGeometry.ValidRoute(Loop() with { Points = [Start, new(47, 16), Start] }, Input(), candidate));
        Assert.False(SmartWalkGeometry.ValidRoute(Loop() with { Points = [SmartWalkGeometry.Offset(Start, 70, 0), candidate.Anchors[1], SmartWalkGeometry.Offset(Start, 70, 180)] }, Input(), candidate));
    }
    [Fact]
    public void TinyLoopWithLongStemScoresBelowEquivalentGenuineLoop()
    {
        var far = SmartWalkGeometry.Offset(Start, 800, 0); var points = new[] { Start, far, SmartWalkGeometry.Offset(far, 25, 90), SmartWalkGeometry.Offset(far, 25, 180), far, Start };
        Assert.True(SmartWalkGeometry.Repetition(points) > .3); Assert.True(SmartWalkGeometry.Score(Input(), Loop() with { Points = points }, []).Total < SmartWalkGeometry.Score(Input(), Loop(), []).Total);
    }
    [Fact]
    public void AntimeridianCorridorAndReverseEdgesAreLocal()
    {
        WalkingCoordinate[] points = [new(0, 179.999), new(0, -179.999), new(0, 179.999)]; Assert.InRange(SmartWalkGeometry.SegmentDistance(new(0, 180), points[0], points[1]), 0, .001); Assert.InRange(SmartWalkGeometry.Repetition(points), .4, .6);
    }
    [Fact]
    public void LongPublicNameFitsExistingSavedStopSchema()
    {
        var name = new string('a', 119) + "🐕" + new string('b', 60); var stop = new SmartWalkPoi(1, "water", name, 46, 15);
        var plan = SmartWalkEligibility.ToPlan("owner", null, new(Input(), Loop(), [stop], [stop], [], 1)); var saved = plan.Stops!.Single(s => s.Type == "water"); Assert.Equal(119, saved.Name.Length); Assert.Equal(name, stop.Name);
    }
    [Fact]
    public async Task PublicPlaceNameEligibilityIsRecheckedAfterPreview()
    {
        await using var f = await SmartFixture.Create();
        var place = new Place { Name = "Park", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15 };
        f.Db.Places.Add(place); await f.Db.SaveChangesAsync();
        var stop = new SmartWalkPoi(place.Id, "park", place.Name, 46, 15);
        Assert.True(await SmartWalkEligibility.PreviewAvailableAsync(f.Db, [stop], default));
        place.Name = "Invalid\u0001name"; await f.Db.SaveChangesAsync();
        Assert.False(await SmartWalkEligibility.PreviewAvailableAsync(f.Db, [stop], default));
    }
    [Fact]
    public async Task FullLengthWaterNameRemainsEligibleButExplicitDogProhibitionDoesNot()
    {
        await using var f = await SmartFixture.Create();
        f.Db.WaterPoints.AddRange(new() { Name = new string('a', 200), Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking },
            new() { Name = "No dogs", Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking, DogAccess = WaterDogAccess.NotAllowed });
        await f.Db.SaveChangesAsync();
        var rows = await new SmartWalkPlanner(f.Db, new FakeRoutes()).LoadNearbyAsync(Input(), default);
        Assert.Equal(200, Assert.Single(rows).Name.Length);
    }
    [Theory]
    [InlineData("water")][InlineData("bin")][InlineData("park")][InlineData("place")]
    public async Task SavedIdentityRejectsReplacementAtOldCoordinates(string kind)
    {
        await using var f = await SmartFixture.Create();
        var water = new WaterPoint { Id = 1, Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking };
        var bin = new TrashBin { Id = 1, Name = "Koš", Latitude = 46, Longitude = 15, IsApproved = true };
        var place = new Place { Id = 1, Name = "Park", Latitude = 46, Longitude = 15, Category = PlaceCategory.DogPark };
        f.Db.AddRange(water, bin, place); await f.Db.SaveChangesAsync();
        var stop = new SmartWalkPoi(1, kind, "Original", 46, 15);
        var plan = SmartWalkEligibility.ToPlan("owner", null, new(Input(), Loop(), [], [stop], [], 1));
        Assert.True(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default));
        water.IsRetired = bin.IsRetired = true; place.IsActive = false; water.Latitude = bin.Latitude = place.Latitude = 47;
        f.Db.AddRange(new WaterPoint { Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking },
            new TrashBin { Name = "Replacement", Latitude = 46, Longitude = 15, IsApproved = true }, new Place { Name = "Replacement", Latitude = 46, Longitude = 15, Category = PlaceCategory.DogPark });
        await f.Db.SaveChangesAsync(); Assert.False(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default));
        water.IsRetired = bin.IsRetired = false; place.IsActive = true; water.Latitude = bin.Latitude = place.Latitude = 46;
        await f.Db.SaveChangesAsync(); Assert.True(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default));
        plan.Stops!.Single(s => s.Type == kind).Reason = "No stable reference";
        Assert.False(await SmartWalkEligibility.StopsAvailableAsync(f.Db, plan.Stops!.ToArray(), default));
    }
    [Fact]
    public void FullOwnerLimiterFailsClosedAndExpiredWindowsFreeCapacity()
    {
        var clock = new Clock(); using var previews = new SmartWalkPreviews(clock);
        for (var i = 0; i < 4096; i++) { Assert.True(previews.TryBegin("owner" + i, out var lease)); lease!.Dispose(); }
        Assert.False(previews.TryBegin("overflow", out _)); Assert.False(previews.TryBegin("overflow", out _));
        clock.Now = clock.Now.AddMinutes(1); Assert.True(previews.TryBegin("overflow", out var after)); after!.Dispose();
    }
    [Fact]
    public void DurationIntentBeatsDoubleDurationWithEveryBonus()
    {
        var facts = new[] { new SmartWalkPoi(1, "water", "Water", 46, 15), new(1, "park", "Park", 46, 15), new(1, "bin", "Bin", 46, 15), new(2, "bin", "Bin", 46, 15) };
        Assert.True(SmartWalkGeometry.Score(Input(), Loop(), []).Total > SmartWalkGeometry.Score(Input(), Loop() with { DistanceMeters = 4500, DurationSeconds = 3600 }, facts).Total);
        Assert.False(SmartWalkGeometry.ValidRoute(Loop() with { DurationSeconds = 3600 }, Input(), SmartWalkGeometry.Candidates(Input(), [])[0]));
    }
    [Fact]
    public async Task LocalPerformanceMeasurementsHaveNoTimingGate()
    {
        await using var f = await SmartFixture.Create();
        f.Db.WaterPoints.AddRange(Enumerable.Range(0, 3000).Select(i => new WaterPoint { Latitude = 46 + (i % 60) * .0001, Longitude = 15 + (i / 60) * .0001, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking })); await f.Db.SaveChangesAsync();
        var planner = new SmartWalkPlanner(f.Db, new FakeRoutes()); var nearby = await planner.LoadNearbyAsync(Input(), default);
        var points = Enumerable.Range(0, 20000).Select(i => { var angle = i / 19999d * Math.PI * 2; return new WalkingCoordinate(46 + .005 * Math.Sin(angle), 15 + .007 * (1 - Math.Cos(angle))); }).ToArray();
        var route = Loop() with { Points = points }; var facts = SmartWalkGeometry.Facts(points, nearby);
        var results = new Dictionary<string, double[]>();
        async Task Measure(string name, Func<Task> work) { await work(); var times = new double[10]; for (var i = 0; i < times.Length; i++) { var sw = System.Diagnostics.Stopwatch.StartNew(); await work(); times[i] = sw.Elapsed.TotalMilliseconds; } results[name] = times; }
        await Measure("candidatesNoProviderMs", () => { Assert.Equal(3, SmartWalkGeometry.Candidates(Input(), nearby).Count); return Task.CompletedTask; });
        await Measure("sqliteInfrastructureQueryMs", async () => { Assert.InRange((await planner.LoadNearbyAsync(Input(), default)).Count, 1, 120); });
        await Measure("score20kVerticesMs", () => { Assert.True(double.IsFinite(SmartWalkGeometry.Score(Input(), route, facts).Total)); return Task.CompletedTask; });
        await Measure("facts20kVerticesMs", () => { SmartWalkGeometry.Facts(points, nearby); return Task.CompletedTask; });
        var output = Environment.GetEnvironmentVariable("DOGGYDROP_SMART_PERFORMANCE");
        if (output != null) await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(new { infrastructureRows = 3000, queriedRows = nearby.Count, vertices = points.Length, results }));
    }
    private sealed class Clock : TimeProvider { public DateTimeOffset Now = DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow() => Now; }
}
internal sealed class SmartFixture : IAsyncDisposable
{
    private readonly SqliteConnection connection; public ApplicationDbContext Db { get; }
    private SmartFixture(SqliteConnection c) { connection = c; Db = new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(c).Options); }
    public static async Task<SmartFixture> Create() { var c = new SqliteConnection("Data Source=:memory:"); await c.OpenAsync(); var f = new SmartFixture(c); await f.Db.Database.EnsureCreatedAsync(); return f; }
    public async ValueTask DisposeAsync() { await Db.DisposeAsync(); await connection.DisposeAsync(); }
}
