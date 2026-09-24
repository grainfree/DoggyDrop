using System.Security.Claims;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Controllers.Api;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkPhotoDeliverySurfaceTests : IDisposable
{
    private const string Original = "https://res.cloudinary.com/example/image/upload/v1/doggydrop-walks/walk.jpg";
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-photo-delivery-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task WalkPhotoApiResponses_UseTransformedUrlForOwnerAndOtherViewer()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = "owner", UserName = "owner@test" },
            new ApplicationUser { Id = "viewer", UserName = "viewer@test" });
        var dog = new Dog { Name = "Test dog", OwnerId = "owner" };
        var walk = new Walk { OwnerId = "owner", Dog = dog, Status = "Completed" };
        db.Walks.Add(walk);
        await db.SaveChangesAsync();
        db.WalkPhotos.Add(new WalkPhoto { WalkId = walk.Id, UserId = "owner", ImageUrl = Original });
        await db.SaveChangesAsync();

        var owner = Controller(db, "owner");
        var viewer = Controller(db, "viewer");
        AssertSafe(Assert.IsType<OkObjectResult>(await owner.Recent()));
        AssertSafe(Assert.IsType<OkObjectResult>(await viewer.Photos(walk.Id)));
        AssertSafe(Assert.IsType<OkObjectResult>(await viewer.Social(walk.Id)));

        var community = new HomeController(NullLogger<HomeController>.Instance, UserManager(db), null!, null!, db,
            null!, null!, new EmptyLeaderboards(), null!, null!);
        community.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "viewer")], "Test"))
            }
        };
        var view = Assert.IsType<ViewResult>(await community.Community());
        var model = Assert.IsType<CommunityViewModel>(view.Model);
        Assert.Equal(new WalkPhoto { ImageUrl = Original }.DeliveryUrl, Assert.Single(model.PhotoFeed).ImageUrl);
        Assert.Equal(new WalkPhoto { ImageUrl = Original }.DeliveryUrl, Assert.Single(model.RecentWalks).CoverPhotoUrl);
    }

    private static void AssertSafe(OkObjectResult result)
    {
        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain(Original, json, StringComparison.Ordinal);
        Assert.Contains("/f_auto,q_auto/", json, StringComparison.Ordinal);
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False").Options);

    private static WalksApiController Controller(ApplicationDbContext db, string userId)
    {
        var manager = UserManager(db);
        var controller = new WalksApiController(db, manager, null!);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId)], "Test"))
            }
        };
        return controller;
    }

    private static UserManager<ApplicationUser> UserManager(ApplicationDbContext db) => new(
            new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db),
            Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(), [], [],
            new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
            new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);

    private sealed class EmptyLeaderboards : ILocalLeaderboardService
    {
        public IReadOnlyList<(string Key, string Name)> Cities => [];
        public Task<LocalLeaderboardBoard> BuildAsync(string? cityKey = null) => Task.FromResult(new LocalLeaderboardBoard());
    }

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
    }
}
