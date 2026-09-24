using System.Security.Claims;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class NearbyDiscoveryServiceTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-discovery-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task ApprovedBin_NotifiesOnlyOptedInUsersInRange_OncePerUserAndBin()
    {
        await SeedAsync("near", "second", "boundary", "outside", "off", "wrong-category", "submitter");
        await using var db = Context();
        var approvedAt = DateTime.UtcNow;
        db.NearbyDiscoveryPreferences.AddRange(
            Preference("near", 0, 0, 1000),
            Preference("second", 0, 0, 3000),
            Preference("boundary", 1000d / 6_371_000 * 180 / Math.PI, 0, 1000),
            Preference("outside", 0.1, 0, 1000),
            Preference("wrong-category", 0, 0, 1000, bins: false),
            Preference("submitter", 0, 0, 1000));
        db.TrashBins.Add(new TrashBin
        {
            Name = "New bin", Latitude = 0, Longitude = 0, IsApproved = true,
            ApprovedAt = approvedAt, UserId = "submitter"
        });
        await db.SaveChangesAsync();
        var bin = await db.TrashBins.AsNoTracking().SingleAsync();

        await new NearbyDiscoveryService(db).NotifyForApprovedBinAsync(bin);
        await new NearbyDiscoveryService(db).NotifyForApprovedBinAsync(bin);
        await using var reopened = Context();
        await new NearbyDiscoveryService(reopened).NotifyForApprovedBinAsync(bin);

        var notifications = await reopened.UserNotifications.AsNoTracking().ToListAsync();
        Assert.Equal(3, notifications.Count);
        Assert.Equal(["boundary", "near", "second"], notifications.Select(item => item.UserId).OrderBy(id => id).ToArray());
        Assert.All(notifications, item =>
        {
            Assert.Equal($"Bin:{bin.Id}", item.SourceKey);
            Assert.Equal("NewBinNearby", item.Type);
            Assert.Equal($"/Map?binId={bin.Id}", item.LinkUrl);
            Assert.DoesNotContain("0,", item.Body);
        });
    }

    [Fact]
    public async Task PendingRejectedAndHistoricalApprovedBins_DoNotNotify()
    {
        await SeedAsync("near");
        await using var db = Context();
        db.NearbyDiscoveryPreferences.Add(Preference("near", 46, 14, 1000));
        await db.SaveChangesAsync();
        var service = new NearbyDiscoveryService(db);
        await service.NotifyForApprovedBinAsync(new TrashBin { Id = 1, IsApproved = false, Latitude = 46, Longitude = 14 });
        await service.NotifyForApprovedBinAsync(new TrashBin { Id = 2, IsApproved = true, ApprovedAt = null, Latitude = 46, Longitude = 14 });
        await service.NotifyForApprovedBinAsync(new TrashBin { Id = 3, IsApproved = false, Latitude = 46, Longitude = 14 });
        Assert.Empty(await db.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task CandidateBoundsIncludeDatelineAndExcludeInvalidCoordinates()
    {
        await SeedAsync("east");
        await using var db = Context();
        db.NearbyDiscoveryPreferences.Add(Preference("east", 0, 179.99, 5000));
        await db.SaveChangesAsync();

        var bin = new TrashBin { Id = 21, IsApproved = true, ApprovedAt = DateTime.UtcNow, Latitude = 0, Longitude = -179.99 };
        await new NearbyDiscoveryService(db).NotifyForApprovedBinAsync(bin);
        Assert.Equal("east", (await db.UserNotifications.AsNoTracking().SingleAsync()).UserId);
        await new NearbyDiscoveryService(db).NotifyForApprovedBinAsync(new TrashBin
        {
            Id = 22, IsApproved = true, ApprovedAt = DateTime.UtcNow, Latitude = double.NaN, Longitude = 0
        });
        Assert.Single(await db.UserNotifications.AsNoTracking().ToListAsync());
        Assert.False(NearbyDiscoveryService.ValidCoordinate(double.NaN, 14));
        Assert.False(NearbyDiscoveryService.ValidCoordinate(46, double.PositiveInfinity));
    }

    [Fact]
    public async Task BaselineChangesNeverBackfillOldApprovals()
    {
        await SeedAsync("near");
        await using var db = Context();
        var baseline = DateTime.UtcNow;
        var preference = Preference("near", 46, 14, 1000);
        preference.EnabledAt = baseline;
        db.NearbyDiscoveryPreferences.Add(preference);
        await db.SaveChangesAsync();
        var old = new TrashBin { Id = 10, IsApproved = true, ApprovedAt = baseline.AddSeconds(-1), Latitude = 46, Longitude = 14 };
        var service = new NearbyDiscoveryService(db);
        await service.NotifyForApprovedBinAsync(old);
        preference.RadiusMeters = 5000;
        await db.SaveChangesAsync();
        await service.NotifyForApprovedBinAsync(old);
        Assert.Empty(await db.UserNotifications.ToListAsync());

        var recent = new TrashBin { Id = 11, IsApproved = true, ApprovedAt = baseline.AddSeconds(1), Latitude = 46, Longitude = 14 };
        await service.NotifyForApprovedBinAsync(recent);
        Assert.Single(await db.UserNotifications.ToListAsync());

        db.NearbyDiscoveryPreferences.Remove(preference);
        await db.SaveChangesAsync();
        var reenabled = Preference("near", 46, 14, 5000);
        reenabled.EnabledAt = baseline.AddSeconds(2);
        db.NearbyDiscoveryPreferences.Add(reenabled);
        await db.SaveChangesAsync();
        await service.NotifyForApprovedBinAsync(recent);
        Assert.Single(await db.UserNotifications.ToListAsync());
    }

    [Fact]
    public async Task ApprovalOnlyTransitionsOnce_AndSubmitterKeepsOnlyApprovalNotice()
    {
        await SeedAsync("near", "submitter");
        await using var db = Context();
        db.NearbyDiscoveryPreferences.AddRange(Preference("near", 46, 14, 1000), Preference("submitter", 46, 14, 1000));
        db.TrashBins.Add(new TrashBin { Name = "Existing", Latitude = 46, Longitude = 14, IsApproved = true });
        db.TrashBins.Add(new TrashBin { Name = "New", Latitude = 46, Longitude = 14, UserId = "submitter" });
        await db.SaveChangesAsync();
        var bin = await db.TrashBins.SingleAsync(item => item.Name == "New");
        var controller = Controller(db);

        await controller.Approve(bin.Id);
        await controller.Approve(bin.Id);

        Assert.Equal(1, await db.UserNotifications.CountAsync(item => item.UserId == "near" && item.Type == "NewBinNearby"));
        Assert.Equal(1, await db.UserNotifications.CountAsync(item => item.UserId == "submitter" && item.Type == "BinApproved"));
        Assert.False(await db.UserNotifications.AnyAsync(item => item.UserId == "submitter" && item.Type == "NewBinNearby"));
        Assert.NotNull((await db.TrashBins.AsNoTracking().SingleAsync(item => item.Id == bin.Id)).ApprovedAt);
    }

    [Fact]
    public async Task RejectedPendingBin_ProducesNoDiscoveryNotice()
    {
        await SeedAsync("near");
        await using var db = Context();
        db.NearbyDiscoveryPreferences.Add(Preference("near", 46, 14, 1000));
        db.TrashBins.Add(new TrashBin { Name = "Rejected", Latitude = 46, Longitude = 14 });
        await db.SaveChangesAsync();
        var binId = (await db.TrashBins.SingleAsync()).Id;
        await Controller(db).Reject(binId, null);
        Assert.Empty(await db.UserNotifications.ToListAsync());
    }

    private static NearbyDiscoveryPreference Preference(string userId, double latitude, double longitude, int radius, bool bins = true) => new()
    {
        UserId = userId, Latitude = latitude, Longitude = longitude, RadiusMeters = radius,
        BinsEnabled = bins, EnabledAt = DateTime.UtcNow.AddDays(-1)
    };

    private async Task SeedAsync(params string[] ids)
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(ids.Select(id => new ApplicationUser { Id = id, UserName = id }));
        await db.SaveChangesAsync();
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False").Options);

    private static MapController Controller(ApplicationDbContext db)
    {
        var notifications = new NotificationService(db);
        var calendar = new GamificationCalendar(TimeProvider.System);
        var controller = new MapController(db, null!, null!, null!, null!, notifications,
            new GamificationService(db, notifications, calendar), null!, null!, null!, calendar, null!);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"))
            }
        };
        controller.Url = new StubUrl();
        return controller;
    }

    public void Dispose() { if (File.Exists(_db)) File.Delete(_db); }

    private sealed class StubUrl : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext context) => "/Map/MyBins";
        public string? Content(string? path) => path;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => "/Map";
        public string? RouteUrl(UrlRouteContext context) => "/Map";
    }
}
