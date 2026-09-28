using System.Data.Common;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PrivacyLifecycleTests
{
    [Fact] public Task FullGraphDeletion() => CheckFullGraph();
    [Fact] public Task DatabaseFailureRollsBack() => CheckRollback();
    [Fact] public Task OwnedExportIncludesAllRoutesWithoutSecrets() => CheckExport();
    [Fact] public Task WalkAndPhotoDeletion() => CheckWalkMedia();

    // These same assertions are run by the isolated PostgreSQL harness against disposable databases.
    internal static async Task CheckFullGraph(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await PrivacyTestDatabase.Create(options);
        var db = fixture.Db;
        await Seed(db);
        // Old rows identify the recipient only. Cleanup must not depend on finding an actor in text.
        db.UserNotifications.Add(new UserNotification { UserId = "bob", Type = "FriendStartedWalk",
            Title = "historic actor", Body = "alice@example.invalid PRIVATE_DOG PRIVATE_PLAN",
            LinkUrl = "/Walks/Details/2147483001" });
        await db.UserNotifications.Where(x => x.UserId == "bob" && x.Type == "WalkComment")
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Title, "historic actor")
                .SetProperty(x => x.LinkUrl, "/Walks/Details/2147483001").SetProperty(x => x.SourceKey, "private-actor"));
        await db.SaveChangesAsync();
        var cleanup = new RecordingCleanup(async () => {
            await using var committed = new ApplicationDbContext(fixture.Options);
            Assert.False(await committed.Users.AnyAsync(x => x.Id == "alice"));
        });
        var result = await new AccountDataDeletion(db, fixture.Users, cleanup).DeleteAsync(await db.Users.SingleAsync(x => x.Id == "alice"));
        Assert.True(result.Succeeded);
        db.ChangeTracker.Clear();
        Assert.Equal("bob", (await db.Users.SingleAsync()).Id);
        var bin = await db.TrashBins.SingleAsync();
        Assert.True(bin.IsApproved); Assert.Null(bin.UserId); Assert.Equal("public-bin-image", bin.ImageUrl);
        Assert.Equal(2, await db.Places.CountAsync());
        Assert.Empty(await db.Friendships.ToListAsync());
        Assert.Equal("bob", (await db.Dogs.SingleAsync()).OwnerId);
        Assert.Equal("bob", (await db.Walks.SingleAsync()).OwnerId);
        Assert.Single(await db.WalkPoints.ToListAsync());
        Assert.Equal("bob", (await db.SavedPlaces.SingleAsync()).UserId);
        Assert.Equal("bob", (await db.PrivacyZones.SingleAsync()).UserId);
        Assert.Equal("bob", (await db.NearbyDiscoveryPreferences.SingleAsync()).UserId);
        Assert.Empty(await db.WalkPhotos.ToListAsync());
        Assert.Empty(await db.WalkComments.ToListAsync()); Assert.Empty(await db.WalkReactions.ToListAsync());
        Assert.Empty(await db.WalkPhotoReactions.ToListAsync()); Assert.Empty(await db.WalkStopCompletions.ToListAsync());
        Assert.Empty(await db.DogParkVisits.ToListAsync()); Assert.Empty(await db.PlaydateRequests.ToListAsync());
        Assert.Empty(await db.PlaydateInterests.ToListAsync()); Assert.Empty(await db.PlannedWalks.ToListAsync());
        Assert.Empty(await db.PlannedWalkStops.ToListAsync()); Assert.Empty(await db.PlannedWalkRoutePoints.ToListAsync());
        Assert.Empty(await db.UserAchievements.ToListAsync()); Assert.Empty(await db.FounderBadges.ToListAsync());
        Assert.Empty(await db.UserXpEvents.ToListAsync()); Assert.Empty(await db.UserStreaks.ToListAsync());
        Assert.Empty(await db.UserGamificationProfiles.ToListAsync()); Assert.Empty(await db.DogXpEvents.ToListAsync());
        Assert.Empty(await db.DogProgressionProfiles.ToListAsync());
        Assert.Empty(await db.UserLogins.ToListAsync()); Assert.Empty(await db.UserTokens.ToListAsync());
        Assert.Empty(await db.UserClaims.ToListAsync()); Assert.Empty(await db.UserRoles.ToListAsync());
        Assert.Single(await db.Roles.ToListAsync());
        var notification = await db.UserNotifications.SingleAsync();
        Assert.Equal("bob", notification.UserId); Assert.Equal(NotificationPrivacy.NeutralBody("WalkComment"), notification.Body);
        Assert.Equal(NotificationPrivacy.NeutralTitle("WalkComment"), notification.Title);
        Assert.Null(notification.LinkUrl); Assert.Null(notification.SourceKey);
        await using (var stream = new MemoryStream())
        {
            await new PersonalDataExport(db).WriteAsync("bob", stream);
            var exported = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            foreach (var secret in new[] { "historic actor", "alice@example.invalid", "PRIVATE_DOG", "PRIVATE_PLAN", "2147483001", "FriendStartedWalk" })
                Assert.DoesNotContain(secret, exported);
        }
        Assert.Equal(new[] { "dog-image", "pending-bin-image", "profile-image", "walk-image" }, cleanup.Urls.Order().ToArray());
        // A replay cannot remove the other account or repeat external cleanup.
        var replay = await new AccountDataDeletion(db, fixture.Users, cleanup).DeleteAsync(new ApplicationUser { Id = "alice" });
        Assert.False(replay.Succeeded); Assert.Equal(1, cleanup.Calls);
    }

    internal static async Task CheckRollback(DbContextOptions<ApplicationDbContext>? options = null)
    {
        var failure = new DeleteFailure();
        await using var fixture = await PrivacyTestDatabase.Create(options, failure);
        await Seed(fixture.Db);
        fixture.Db.UserNotifications.Add(new UserNotification { UserId = "bob", Type = "FriendStartedWalk",
            Title = "historic actor", Body = "PRIVATE_PLAN", LinkUrl = "/Walks/Details/2147483001" });
        await fixture.Db.SaveChangesAsync();
        failure.Enabled = true;
        var cleanup = new RecordingCleanup();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AccountDataDeletion(fixture.Db, fixture.Users, cleanup)
            .DeleteAsync(fixture.Db.Users.Single(x => x.Id == "alice")));
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        Assert.Equal(2, await fixture.Db.Users.CountAsync()); Assert.Equal(2, await fixture.Db.Dogs.CountAsync());
        Assert.Equal(2, await fixture.Db.Walks.CountAsync()); Assert.Equal(1202, await fixture.Db.WalkPoints.CountAsync());
        Assert.Equal(2, await fixture.Db.TrashBins.CountAsync(x => x.UserId == "alice"));
        Assert.Equal(2, await fixture.Db.Friendships.CountAsync()); Assert.Single(await fixture.Db.WalkPhotos.ToListAsync());
        Assert.Contains("historic actor", (await fixture.Db.UserNotifications.SingleAsync(x => x.UserId == "bob" && x.Type == "WalkComment")).Body);
        Assert.Equal("PRIVATE_PLAN", (await fixture.Db.UserNotifications.SingleAsync(x => x.Type == "FriendStartedWalk")).Body);
        Assert.Equal(0, cleanup.Calls);
    }

    internal static async Task CheckExport(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await PrivacyTestDatabase.Create(options);
        await Seed(fixture.Db);
        await using var stream = new MemoryStream();
        await new PersonalDataExport(fixture.Db).WriteAsync("alice", stream);
        var text = System.Text.Encoding.UTF8.GetString(stream.ToArray());
        using var document = JsonDocument.Parse(text);
        var root = document.RootElement;
        Assert.Equal("alice@example.invalid", root.GetProperty("Account").GetProperty("Email").GetString());
        Assert.Single(root.GetProperty("Dogs").EnumerateArray());
        Assert.Single(root.GetProperty("Walks").EnumerateArray());
        Assert.Equal(1201, root.GetProperty("WalkPoints").GetArrayLength());
        Assert.All(root.GetProperty("WalkPoints").EnumerateArray(), p => Assert.Equal(46.5547, p.GetProperty("Latitude").GetDouble()));
        Assert.Single(root.GetProperty("SavedPlaces").EnumerateArray());
        Assert.Single(root.GetProperty("PublicPlaceReferences").EnumerateArray());
        Assert.Single(root.GetProperty("PrivacyZone").EnumerateArray());
        Assert.Equal(2, root.GetProperty("BinContributions").GetArrayLength());
        foreach (var section in new[] { "Notifications", "WalkPhotos", "PlannedWalks", "PlannedWalkStops", "PlannedWalkRoutePoints",
            "Achievements", "FounderBadges", "XpEvents", "Streaks", "GamificationProfile", "DogProgression", "DogXpEvents", "ParkVisits",
            "PlaydateRequests", "PlaydateInterests", "WalkReactions", "WalkComments", "WalkPhotoReactions", "WalkStopCompletions", "ExternalLogins", "Roles", "ProfileClaims" })
            Assert.Single(root.GetProperty(section).EnumerateArray());
        foreach (var excluded in new[] { "PasswordHash", "SecurityStamp", "AuthenticatorKey", "RecoveryCodes", "authentication-secret", "token-secret",
            "bob@example.invalid", "bob-private-dog", "bob-private-profile", "bob-private-notification", "source-private-contact", "88.7654321" })
            Assert.DoesNotContain(excluded, text);
    }

    internal static async Task CheckWalkMedia(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await PrivacyTestDatabase.Create(options);
        await Seed(fixture.Db);
        var walk = await fixture.Db.Walks.SingleAsync(x => x.OwnerId == "alice");
        var photo = await fixture.Db.WalkPhotos.SingleAsync();
        var cleanup = new RecordingCleanup(async () => {
            await using var committed = new ApplicationDbContext(fixture.Options);
            Assert.False(await committed.WalkPhotos.AnyAsync(x => x.Id == photo.Id));
        });
        var deletion = new WalkDataDeletion(fixture.Db, cleanup);
        Assert.False(await deletion.DeletePhotoAsync("bob", walk.Id, photo.Id));
        Assert.Equal(0, cleanup.Calls);
        Assert.True(await deletion.DeletePhotoAsync("alice", walk.Id, photo.Id));
        Assert.False(await deletion.DeletePhotoAsync("alice", walk.Id, photo.Id));
        Assert.Equal(1, cleanup.Calls);
        fixture.Db.WalkPhotos.AddRange(new WalkPhoto { UserId="alice", WalkId=walk.Id, ImageUrl="shared-walk-image" },
            new WalkPhoto { UserId="alice", WalkId=walk.Id, ImageUrl="shared-walk-image" });
        await fixture.Db.SaveChangesAsync();
        Assert.False(await deletion.DeleteWalkAsync("bob", walk.Id));
        Assert.True(await deletion.DeleteWalkAsync("alice", walk.Id));
        Assert.False(await deletion.DeleteWalkAsync("alice", walk.Id));
        Assert.Equal(2, cleanup.Calls);
        Assert.Empty(await fixture.Db.WalkPhotos.ToListAsync());
        Assert.Single(await fixture.Db.WalkPoints.ToListAsync());
        Assert.Equal(2, await fixture.Db.Users.CountAsync());
    }

    internal static async Task Seed(ApplicationDbContext db)
    {
        var alice = new ApplicationUser { Id="alice", UserName="alice@example.invalid", Email="alice@example.invalid", DisplayName="Alice",
            ProfileImageUrl="profile-image", PasswordHash="authentication-secret", SecurityStamp="authentication-secret" };
        var bob = new ApplicationUser { Id="bob", UserName="bob@example.invalid", Email="bob@example.invalid", ProfileImageUrl="bob-private-profile" };
        var dog = new Dog { Owner=alice, Name="Alice dog", PhotoUrl="dog-image" };
        var bobDog = new Dog { Owner=bob, Name="bob-private-dog" };
        var walk = new Walk { Owner=alice, Dog=dog, Status="Completed", DistanceMeters=1234, EndedAt=DateTime.UtcNow };
        var bobWalk = new Walk { Owner=bob, Dog=bobDog };
        db.WalkPoints.AddRange(Enumerable.Range(0,1201).Select(i => new WalkPoint { Walk=walk, Latitude=46.5547, Longitude=15.6459, RecordedAt=DateTime.UtcNow.AddSeconds(i) }));
        db.WalkPoints.Add(new WalkPoint { Walk=bobWalk, Latitude=88.7654321, Longitude=111 });
        var photo = new WalkPhoto { User=alice, Walk=walk, ImageUrl="walk-image" };
        db.WalkPhotos.Add(photo);
        var source = new DataSource { Name="Source", ContactEmail="source-private-contact" };
        var place = new Place { Name="Public park", Category=PlaceCategory.DogPark, Latitude=46, Longitude=15, DataSource=source };
        db.SavedPlaces.AddRange(new SavedPlace { User=alice, Place=place }, new SavedPlace { User=bob, Place=new Place { Name="Other saved park" } });
        db.PrivacyZones.AddRange(new PrivacyZone { User=alice, Latitude=46, Longitude=15, RadiusMeters=300 }, new PrivacyZone { User=bob, Latitude=88.7654321, Longitude=111, RadiusMeters=300 });
        db.NearbyDiscoveryPreferences.AddRange(new NearbyDiscoveryPreference { User=alice }, new NearbyDiscoveryPreference { User=bob, Latitude=88.7654321 });
        db.TrashBins.AddRange(new TrashBin { User=alice, Name="Approved", IsApproved=true, ImageUrl="public-bin-image" },
            new TrashBin { User=alice, Name="Pending", ImageUrl="pending-bin-image" });
        db.Friendships.AddRange(new Friendship { Requester=alice, Addressee=bob, Status="Accepted" }, new Friendship { Requester=bob, Addressee=alice });
        db.UserNotifications.AddRange(new UserNotification { User=alice, Type="PlaydateInvite", Body="Legacy sender: bob@example.invalid" }, new UserNotification { User=bob, Type="WalkComment", Body="historic actor commented; bob-private-notification" });
        db.UserAchievements.Add(new UserAchievement { User=alice, AchievementKey="one" });
        db.UserXpEvents.Add(new UserXpEvent { User=alice }); db.UserStreaks.Add(new UserStreak { User=alice });
        db.UserGamificationProfiles.Add(new UserGamificationProfile { User=alice }); db.FounderBadges.Add(new FounderBadge { User=alice, AreaKey="test" });
        db.DogProgressionProfiles.Add(new DogProgressionProfile { Dog=dog }); db.DogXpEvents.Add(new DogXpEvent { Dog=dog });
        var plan = new PlannedWalk { Owner=alice, Dog=dog, Title="Own plan" };
        var stop = new PlannedWalkStop { PlannedWalk=plan, Name="Own stop" };
        db.PlannedWalkRoutePoints.Add(new PlannedWalkRoutePoint { PlannedWalk=plan, Latitude=46 });
        db.WalkStopCompletions.Add(new WalkStopCompletion { User=alice, Walk=walk, PlannedWalkStop=stop });
        db.WalkReactions.Add(new WalkReaction { User=alice, Walk=bobWalk });
        db.WalkComments.Add(new WalkComment { User=alice, Walk=bobWalk, Body="Own comment" });
        db.WalkPhotoReactions.Add(new WalkPhotoReaction { User=alice, WalkPhoto=photo });
        db.DogParkVisits.Add(new DogParkVisit { User=alice, Dog=dog, ParkName="Public park" });
        var request = new PlaydateRequest { Owner=alice, Dog=dog };
        db.PlaydateInterests.Add(new PlaydateInterest { Owner=alice, Dog=dog, PlaydateRequest=request });
        db.Roles.Add(new IdentityRole { Id="member", Name="Member" });
        await db.SaveChangesAsync();
        db.UserLogins.Add(new IdentityUserLogin<string> { UserId="alice", LoginProvider="Google", ProviderKey="external-identifier" });
        db.UserTokens.AddRange(new IdentityUserToken<string> { UserId="alice", LoginProvider="Google", Name="access_token", Value="token-secret" },
            new IdentityUserToken<string> { UserId="alice", LoginProvider="[AspNetUserStore]", Name="AuthenticatorKey", Value="authentication-secret" },
            new IdentityUserToken<string> { UserId="alice", LoginProvider="[AspNetUserStore]", Name="RecoveryCodes", Value="authentication-secret" });
        db.UserClaims.Add(new IdentityUserClaim<string> { UserId="alice", ClaimType=System.Security.Claims.ClaimTypes.GivenName, ClaimValue="Alice" });
        db.UserRoles.Add(new IdentityUserRole<string> { UserId="alice", RoleId="member" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }

    private sealed class DeleteFailure : SaveChangesInterceptor
    {
        public bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (Enabled && data.Context!.ChangeTracker.Entries<ApplicationUser>().Any(x => x.State == EntityState.Deleted))
                throw new InvalidOperationException("Injected database failure");
            return base.SavingChangesAsync(data, result, ct);
        }
    }
}

internal sealed class RecordingCleanup(Func<Task>? onCleanup = null) : IUserMediaCleanup
{
    public int Calls;
    public List<string> Urls = [];
    public async Task CleanupAsync(IEnumerable<string?> urls)
    {
        Calls++; Urls.AddRange(urls.OfType<string>().Distinct());
        if (onCleanup != null) await onCleanup();
    }
}

internal sealed class PrivacyTestDatabase : IAsyncDisposable
{
    private readonly string? path;
    private readonly ServiceProvider services;
    public DbContextOptions<ApplicationDbContext> Options { get; }
    public ApplicationDbContext Db { get; }
    public UserManager<ApplicationUser> Users => services.GetRequiredService<UserManager<ApplicationUser>>();
    private PrivacyTestDatabase(DbContextOptions<ApplicationDbContext>? options, IInterceptor? interceptor)
    {
        var builder = options == null ? new DbContextOptionsBuilder<ApplicationDbContext>() : new DbContextOptionsBuilder<ApplicationDbContext>(options);
        if (options == null) { path=Path.Combine(Path.GetTempPath(), $"privacy-{Guid.NewGuid():N}.db"); builder.UseSqlite($"Data Source={path};Pooling=False"); }
        if (interceptor != null) builder.AddInterceptors(interceptor);
        Options=builder.Options; Db=new ApplicationDbContext(Options);
        var collection=new ServiceCollection(); collection.AddLogging(); collection.AddSingleton(Db);
        collection.AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        services=collection.BuildServiceProvider();
    }
    internal static async Task<PrivacyTestDatabase> Create(DbContextOptions<ApplicationDbContext>? options = null, IInterceptor? interceptor = null)
    {
        var fixture=new PrivacyTestDatabase(options, interceptor); await fixture.Db.Database.EnsureCreatedAsync(); return fixture;
    }
    public async ValueTask DisposeAsync() { await services.DisposeAsync(); await Db.DisposeAsync(); if(path!=null) File.Delete(path); }
}
