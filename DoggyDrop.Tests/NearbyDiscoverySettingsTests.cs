using System.Security.Claims;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class NearbyDiscoverySettingsTests : IDisposable
{
    private const string Owner = "discovery-owner";
    private const string Other = "discovery-other";
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-discovery-settings-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task DefaultIsDisabled_AndOwnersOnlySeeTheirOwnArea()
    {
        await SeedAsync();
        await using var db = Context();
        var disabled = Assert.IsType<PrivacyZoneSettingsViewModel>(Assert.IsType<ViewResult>(await Controller(db, Owner).Settings()).Model);
        Assert.False(disabled.Discovery.IsEnabled);
        Assert.Null(disabled.Discovery.Latitude);

        db.NearbyDiscoveryPreferences.Add(new NearbyDiscoveryPreference
        {
            UserId = Other, Latitude = 46.1, Longitude = 14.1, RadiusMeters = 3000,
            BinsEnabled = true, EnabledAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        var owner = Assert.IsType<PrivacyZoneSettingsViewModel>(Assert.IsType<ViewResult>(await Controller(db, Owner).Settings()).Model);
        Assert.False(owner.Discovery.IsEnabled);
        var foreign = Assert.IsType<PrivacyZoneSettingsViewModel>(Assert.IsType<ViewResult>(await Controller(db, Other).Settings()).Model);
        Assert.True(foreign.Discovery.IsEnabled);
        Assert.Equal(46.1, foreign.Discovery.Latitude);
    }

    [Fact]
    public async Task EnableUpdateDisableAndReenable_KeepOrResetBaselineAsAppropriate()
    {
        await SeedAsync();
        await using var db = Context();
        var controller = Controller(db, Owner);
        controller.HttpContext.Request.QueryString = new QueryString($"?userId={Other}");
        await controller.SaveNearbyDiscovery(Input());
        var first = await db.NearbyDiscoveryPreferences.AsNoTracking().SingleAsync();
        Assert.Equal(Owner, first.UserId);
        Assert.True(first.EnabledAt > DateTime.UtcNow.AddMinutes(-1));
        Assert.Null(typeof(NearbyDiscoverySettingsInput).GetProperty("UserId"));

        await controller.SaveNearbyDiscovery(Input(radius: 5000));
        var updated = await db.NearbyDiscoveryPreferences.AsNoTracking().SingleAsync();
        Assert.Equal(5000, updated.RadiusMeters);
        Assert.Equal(first.EnabledAt, updated.EnabledAt);

        await controller.SaveNearbyDiscovery(new NearbyDiscoverySettingsInput { Enabled = false });
        Assert.Empty(await db.NearbyDiscoveryPreferences.ToListAsync());
        db.ChangeTracker.Clear();
        await controller.SaveNearbyDiscovery(Input());
        var reenabled = await db.NearbyDiscoveryPreferences.AsNoTracking().SingleAsync();
        Assert.True(reenabled.EnabledAt >= first.EnabledAt);
    }

    [Theory]
    [InlineData("91", "14", 1000, true)]
    [InlineData("46", "181", 1000, true)]
    [InlineData("NaN", "14", 1000, true)]
    [InlineData("Infinity", "14", 1000, true)]
    [InlineData("46", "14", 2000, true)]
    [InlineData("46", "14", 3000, false)]
    [InlineData(null, "14", 3000, true)]
    public async Task InvalidUpdate_DoesNotEraseExistingArea(string? latitude, string? longitude, int radius, bool bins)
    {
        await SeedAsync();
        await using var db = Context();
        db.NearbyDiscoveryPreferences.Add(new NearbyDiscoveryPreference
        {
            UserId = Owner, Latitude = 46.1, Longitude = 14.1, RadiusMeters = 1000,
            BinsEnabled = true, EnabledAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();

        await Controller(db, Owner).SaveNearbyDiscovery(new NearbyDiscoverySettingsInput
        {
            Enabled = true, Latitude = latitude, Longitude = longitude, RadiusMeters = radius, BinsEnabled = bins
        });

        var saved = await db.NearbyDiscoveryPreferences.AsNoTracking().SingleAsync();
        Assert.Equal(46.1, saved.Latitude);
        Assert.Equal(14.1, saved.Longitude);
        Assert.Equal(1000, saved.RadiusMeters);
    }

    [Fact]
    public async Task ForeignPreferenceRemainsUntouched()
    {
        await SeedAsync();
        await using var db = Context();
        db.NearbyDiscoveryPreferences.Add(new NearbyDiscoveryPreference
        {
            UserId = Other, Latitude = 46, Longitude = 14, RadiusMeters = 1000,
            BinsEnabled = true, EnabledAt = DateTime.UtcNow.AddDays(-1)
        });
        await db.SaveChangesAsync();
        await Controller(db, Owner).SaveNearbyDiscovery(Input());
        await Controller(db, Owner).SaveNearbyDiscovery(new NearbyDiscoverySettingsInput { Enabled = false });
        Assert.Equal(Other, (await db.NearbyDiscoveryPreferences.AsNoTracking().SingleAsync()).UserId);
    }

    [Fact]
    public void MutationRequiresAuthenticationAndAntiforgery()
    {
        var write = typeof(HomeController).GetMethod(nameof(HomeController.SaveNearbyDiscovery))!;
        Assert.Single(write.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true));
        Assert.Single(write.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
        Assert.Single(write.GetCustomAttributes(typeof(HttpPostAttribute), true));
    }

    private static NearbyDiscoverySettingsInput Input(int radius = 3000) => new()
    {
        Enabled = true, Latitude = "46.1", Longitude = "14.1", RadiusMeters = radius, BinsEnabled = true
    };

    private async Task SeedAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new ApplicationUser { Id = Owner, UserName = Owner }, new ApplicationUser { Id = Other, UserName = Other });
        await db.SaveChangesAsync();
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False").Options);

    private static HomeController Controller(ApplicationDbContext db, string userId)
    {
        var manager = new UserManager<ApplicationUser>(
            new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);
        var controller = new HomeController(NullLogger<HomeController>.Instance, manager, null!, null!, db,
            null!, null!, null!, null!, null!);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TestTempDataProvider());
        return controller;
    }

    public void Dispose() { if (File.Exists(_db)) File.Delete(_db); }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
