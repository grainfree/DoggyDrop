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

public sealed class PrivacyZoneSettingsTests : IDisposable
{
    private const string OwnerId = "privacy-owner";
    private const string OtherId = "privacy-other";
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-privacy-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task Settings_ReturnsOnlyCurrentUsersZone()
    {
        await SeedAsync();
        await using var db = Context();
        db.PrivacyZones.Add(new PrivacyZone { UserId = OwnerId, Latitude = 46.1, Longitude = 14.1, RadiusMeters = 300 });
        await db.SaveChangesAsync();

        var otherView = Assert.IsType<ViewResult>(await Controller(db, OtherId).Settings());
        var otherModel = Assert.IsType<PrivacyZoneSettingsViewModel>(otherView.Model);
        Assert.False(otherModel.IsEnabled);
        Assert.Null(otherModel.Latitude);
        Assert.Null(otherModel.Longitude);

        var ownerView = Assert.IsType<ViewResult>(await Controller(db, OwnerId).Settings());
        var ownerModel = Assert.IsType<PrivacyZoneSettingsViewModel>(ownerView.Model);
        Assert.True(ownerModel.IsEnabled);
        Assert.Equal(46.1, ownerModel.Latitude);
        Assert.Equal(14.1, ownerModel.Longitude);
    }

    [Fact]
    public async Task SavePrivacyZone_CreatesAndUpdatesOnlyCurrentUsersZone()
    {
        await SeedAsync();
        await using var db = Context();
        db.PrivacyZones.Add(new PrivacyZone { UserId = OtherId, Latitude = 45, Longitude = 13, RadiusMeters = 200 });
        await db.SaveChangesAsync();

        var owner = Controller(db, OwnerId);
        Assert.IsType<RedirectToActionResult>(await owner.SavePrivacyZone(new PrivacyZoneSettingsInput
        {
            Enabled = true, Latitude = "46.1", Longitude = "14.1", RadiusMeters = 300
        }));
        Assert.IsType<RedirectToActionResult>(await owner.SavePrivacyZone(new PrivacyZoneSettingsInput
        {
            Enabled = true, Latitude = "46.2", Longitude = "14.2", RadiusMeters = 500
        }));

        Assert.Equal(2, await db.PrivacyZones.CountAsync());
        var saved = await db.PrivacyZones.AsNoTracking().SingleAsync(zone => zone.UserId == OwnerId);
        Assert.Equal(46.2, saved.Latitude);
        Assert.Equal(500, saved.RadiusMeters);
        var other = await db.PrivacyZones.AsNoTracking().SingleAsync(zone => zone.UserId == OtherId);
        Assert.Equal(45, other.Latitude);
        Assert.Equal(200, other.RadiusMeters);
    }

    [Fact]
    public async Task SavePrivacyZone_IgnoresSpoofedUserId()
    {
        await SeedAsync();
        await using var db = Context();
        var controller = Controller(db, OwnerId);
        controller.HttpContext.Request.QueryString = new QueryString($"?userId={OtherId}");
        await controller.SavePrivacyZone(new PrivacyZoneSettingsInput
        {
            Enabled = true, Latitude = "46.1", Longitude = "14.1", RadiusMeters = 300
        });

        Assert.True(await db.PrivacyZones.AnyAsync(zone => zone.UserId == OwnerId));
        Assert.False(await db.PrivacyZones.AnyAsync(zone => zone.UserId == OtherId));
        Assert.Null(typeof(PrivacyZoneSettingsInput).GetProperty("UserId"));
    }

    [Fact]
    public async Task InvalidUpdate_PreservesPreviouslySavedZone()
    {
        await SeedAsync();
        await using var db = Context();
        db.PrivacyZones.Add(new PrivacyZone { UserId = OwnerId, Latitude = 46.1, Longitude = 14.1, RadiusMeters = 300 });
        await db.SaveChangesAsync();

        await Controller(db, OwnerId).SavePrivacyZone(new PrivacyZoneSettingsInput
        {
            Enabled = true, Latitude = "91", Longitude = "14.2", RadiusMeters = 1000
        });

        var saved = await db.PrivacyZones.AsNoTracking().SingleAsync(zone => zone.UserId == OwnerId);
        Assert.Equal(46.1, saved.Latitude);
        Assert.Equal(300, saved.RadiusMeters);
    }

    [Fact]
    public async Task Disable_RemovesOnlyCurrentUsersZone()
    {
        await SeedAsync();
        await using var db = Context();
        db.PrivacyZones.AddRange(
            new PrivacyZone { UserId = OwnerId, Latitude = 46.1, Longitude = 14.1, RadiusMeters = 300 },
            new PrivacyZone { UserId = OtherId, Latitude = 45, Longitude = 13, RadiusMeters = 200 });
        await db.SaveChangesAsync();

        await Controller(db, OwnerId).SavePrivacyZone(new PrivacyZoneSettingsInput { Enabled = false });

        Assert.False(await db.PrivacyZones.AnyAsync(zone => zone.UserId == OwnerId));
        Assert.True(await db.PrivacyZones.AnyAsync(zone => zone.UserId == OtherId));
    }

    [Fact]
    public void SettingsEndpoints_RequireAuthenticationAndWriteRequiresAntiforgery()
    {
        Assert.NotNull(typeof(HomeController).GetMethod(nameof(HomeController.Settings))!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).SingleOrDefault());
        var write = typeof(HomeController).GetMethod(nameof(HomeController.SavePrivacyZone))!;
        Assert.NotNull(write.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true).SingleOrDefault());
        Assert.NotNull(write.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true).SingleOrDefault());
        Assert.NotNull(write.GetCustomAttributes(typeof(HttpPostAttribute), true).SingleOrDefault());
    }

    private async Task SeedAsync()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = OwnerId, UserName = "privacy-owner@test" },
            new ApplicationUser { Id = OtherId, UserName = "privacy-other@test" });
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

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
