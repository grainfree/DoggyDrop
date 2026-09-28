using System.Net;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DoggyDrop.Tests;

public sealed partial class PrivacyHttpTests
{
    private const string UnsafeNotification = "identifiable@example.com identifiable PRIVATE_DOG PRIVATE_PLAN 2147483001";
    private const string UnsafeNotificationLink = "/Walks/Details/2147483001";

    private static void AssertNoNotificationSecrets(string value)
    {
        foreach (var secret in new[] { "identifiable", "PRIVATE_DOG", "PRIVATE_PLAN", "2147483001" })
            Assert.DoesNotContain(secret, WebUtility.HtmlDecode(value), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string> Download(HttpClient client)
    {
        var token = await Token(client, Manage + "PersonalData");
        var response = await client.PostAsync(Manage + "DownloadPersonalData", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    [Theory]
    [InlineData("FriendStartedWalk", false)] [InlineData("FriendStartedWalk", true)]
    [InlineData("WalkComment", false)] [InlineData("WalkComment", true)]
    [InlineData("WalkReaction", false)] [InlineData("WalkReaction", true)]
    [InlineData("PlaydateInvite", false)] [InlineData("PlaydateInvite", true)]
    [InlineData("PlaydateInterest", false)] [InlineData("PlaydateInterest", true)]
    public async Task HistoricalSensitiveNotificationsAreSafeOnEveryOutputAndMarkRead(string type, bool read)
    {
        int id;
        var created = new DateTime(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UserNotifications.ExecuteDeleteAsync();
            var row = new UserNotification { UserId = "bob", Type = type, Title = UnsafeNotification,
                Body = UnsafeNotification, LinkUrl = UnsafeNotificationLink, IsRead = read, CreatedAt = created };
            db.UserNotifications.Add(row);
            await db.SaveChangesAsync(); id = row.Id;
        }
        using var client = Client("bob");
        foreach (var path in new[] { "/api/notifications", "/api/notifications/center" })
        {
            var json = await client.GetStringAsync(path);
            AssertNoNotificationSecrets(json);
            using var doc = JsonDocument.Parse(json);
            foreach (var collection in path.EndsWith("center") ? new[] { "latest", "latestUnread" } : new[] { "notifications" })
            {
                var rows = doc.RootElement.GetProperty(collection).EnumerateArray().ToArray();
                if (collection == "latestUnread" && read) { Assert.Empty(rows); continue; }
                var row = Assert.Single(rows);
                Assert.Equal(id, row.GetProperty("id").GetInt32());
                Assert.Equal(type, row.GetProperty("type").GetString());
                Assert.Equal(read, row.GetProperty("isRead").GetBoolean());
                Assert.Equal(created, row.GetProperty("createdAt").GetDateTime());
                Assert.Equal(NotificationPrivacy.NeutralTitle(type), row.GetProperty("title").GetString());
                Assert.Equal(NotificationPrivacy.NeutralBody(type), row.GetProperty("body").GetString());
                Assert.Equal(NotificationPrivacy.Link(type, null), row.GetProperty("linkUrl").GetString());
                if (collection != "notifications")
                {
                    Assert.Equal(NotificationPrivacy.NeutralTitle(type), row.GetProperty("displayTitle").GetString());
                    Assert.Equal(NotificationPrivacy.NeutralBody(type), row.GetProperty("displayBody").GetString());
                }
            }
        }
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/Notifications"));
        AssertNoNotificationSecrets(html);
        Assert.Contains(NotificationPrivacy.NeutralBody(type)!, html);
        var exported = await Download(client);
        AssertNoNotificationSecrets(exported);
        using (var doc = JsonDocument.Parse(exported))
        {
            var row = doc.RootElement.GetProperty("Notifications").EnumerateArray().Single(n => n.GetProperty("Id").GetInt32() == id);
            Assert.Equal(type, row.GetProperty("Type").GetString());
            Assert.Equal(read, row.GetProperty("IsRead").GetBoolean());
            Assert.Equal(created, row.GetProperty("CreatedAt").GetDateTime());
            Assert.Equal(NotificationPrivacy.NeutralTitle(type), row.GetProperty("Title").GetString());
            Assert.Equal(NotificationPrivacy.NeutralBody(type), row.GetProperty("Body").GetString());
            Assert.Equal(NotificationPrivacy.Link(type, null), row.GetProperty("LinkUrl").GetString());
        }
        var marked = await client.PostAsync($"/api/notifications/{id}/read", null);
        Assert.Equal(HttpStatusCode.OK, marked.StatusCode);
        var markedJson = await marked.Content.ReadAsStringAsync(); AssertNoNotificationSecrets(markedJson);
        using (var doc = JsonDocument.Parse(markedJson))
        {
            Assert.True(doc.RootElement.GetProperty("isRead").GetBoolean());
            Assert.Equal(NotificationPrivacy.Link(type, null), doc.RootElement.GetProperty("linkUrl").GetString());
        }
        var redirect = await client.PostAsync("/Notifications/MarkRead", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["id"] = id.ToString(), ["__RequestVerificationToken"] = await Token(client, "/Notifications")
        }));
        Assert.Equal(HttpStatusCode.Redirect, redirect.StatusCode);
        Assert.Equal(NotificationPrivacy.Link(type, null) ?? "/Notifications", redirect.Headers.Location!.OriginalString);
        using var stranger = Client("alice");
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.PostAsync($"/api/notifications/{id}/read", null)).StatusCode);
    }

    [Theory]
    [InlineData("Start")] [InlineData("StartPlanned")]
    public async Task BothRealWalkStartPathsKeepOwnerPlanAndDoNotBroadcastToFriends(string action)
    {
        var ids = await PrivateIds();
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.True(await db.Friendships.AnyAsync(x => x.RequesterId == "alice" && x.AddresseeId == "bob" && x.Status == "Accepted"));
            await db.UserNotifications.ExecuteDeleteAsync();
        }
        using var owner = Client("alice");
        var response = await owner.PostAsync("/Walks/" + action, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["dogId"] = ids.Dog.ToString(), ["plannedWalkId"] = ids.Plan.ToString(), ["area"] = "maribor",
            ["distanceKm"] = "2", ["walkStyle"] = "balanced",
            ["__RequestVerificationToken"] = await Token(owner, Manage + "PersonalData")
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Walks/Active/", response.Headers.Location!.OriginalString);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(response.Headers.Location)).StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var walk = await db.Walks.Include(x => x.PlannedWalk).SingleAsync(x => x.OwnerId == "alice" && x.Status == "Active");
            Assert.Equal(ids.Dog, walk.DogId); Assert.NotNull(walk.PlannedWalk);
            Assert.Equal("alice", walk.PlannedWalk.OwnerId); Assert.NotNull(walk.PlannedWalk.UsedAt);
            if (action == "Start") Assert.Equal(ids.Plan, walk.PlannedWalkId);
            Assert.False(await db.UserNotifications.AnyAsync(x => x.Type == "FriendStartedWalk" || x.UserId == "bob"));
        }
        using var friend = Client("bob");
        foreach (var path in new[] { "/api/notifications", "/api/notifications/center", "/Notifications" })
        {
            var body = await friend.GetStringAsync(path);
            Assert.DoesNotContain(path == "/Notifications" ? "notification-card__icon--FriendStartedWalk" : "FriendStartedWalk", body);
            AssertNoPrivateContent(body);
        }
        var export = await Download(friend);
        Assert.DoesNotContain("FriendStartedWalk", export); AssertNoPrivateContent(export);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task NotificationServiceCannotReintroducePrivateWalkBroadcast(bool unique)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();
        if (unique) await service.CreateUniqueRecentAsync("bob", "FriendStartedWalk", UnsafeNotification, UnsafeNotification, UnsafeNotificationLink);
        else await service.CreateAsync("bob", "FriendStartedWalk", UnsafeNotification, UnsafeNotification, UnsafeNotificationLink);
        Assert.False(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserNotifications.AnyAsync(x => x.Type == "FriendStartedWalk"));
    }

    [Theory]
    [InlineData("WalkComment")] [InlineData("WalkReaction")]
    [InlineData("PlaydateInvite")] [InlineData("PlaydateInterest")]
    public async Task FutureActorNotificationWritesStoreOnlySafeTextAndLinks(string type)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await service.CreateAsync("bob", type, UnsafeNotification, UnsafeNotification, UnsafeNotificationLink);
        var row = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().UserNotifications.OrderByDescending(x => x.Id).FirstAsync();
        Assert.Equal(NotificationPrivacy.NeutralTitle(type), row.Title);
        Assert.Equal(NotificationPrivacy.NeutralBody(type), row.Body);
        Assert.Equal(NotificationPrivacy.Link(type, null), row.LinkUrl);
    }

    [Theory]
    [InlineData("General")] [InlineData("BinApproved")] [InlineData("NewBinNearby")]
    [InlineData("FriendRequest")] [InlineData("FriendAccepted")]
    [InlineData("Achievement")] [InlineData("Achievement:first_walk")]
    [InlineData("LevelUp:2")] [InlineData("Streak:Walk:3")] [InlineData("FounderBadge:maribor")]
    [InlineData("WalkReminder")] [InlineData("PopularParkNearby")]
    [InlineData("FirstDogProfile")] [InlineData("NearbyMapTips")] [InlineData("WeatherAlert")]
    public async Task SafeSystemOwnAchievementAndGenericFriendNotificationsRemainUseful(string type)
    {
        const string body = "Varno obvestilo ostane uporabno.";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UserNotifications.ExecuteDeleteAsync();
            await scope.ServiceProvider.GetRequiredService<INotificationService>().CreateAsync("bob", type, "Obvestilo", body, "/Friends");
            var stored = await db.UserNotifications.SingleAsync();
            Assert.Equal(body, stored.Body); Assert.Equal("/Friends", stored.LinkUrl);
        }
        using var client = Client("bob");
        foreach (var path in new[] { "/api/notifications", "/api/notifications/center", "/Notifications" })
        {
            var result = WebUtility.HtmlDecode(await client.GetStringAsync(path));
            Assert.Contains(body, result); Assert.Contains("/Friends", result);
        }
        Assert.Contains(body, await Download(client));
    }

    [Fact]
    public async Task AccountDeletionPurgesObsoleteBroadcastAndNeutralizesOtherLegacyStorageAndOutputs()
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UserNotifications.ExecuteDeleteAsync();
            foreach (var type in NotificationPrivacy.ActorMessageTypes)
                db.UserNotifications.Add(new UserNotification { UserId = "bob", Type = type, Title = UnsafeNotification,
                    Body = UnsafeNotification, LinkUrl = UnsafeNotificationLink, SourceKey = type + ":private-actor" });
            db.UserNotifications.Add(new UserNotification { UserId = "bob", Type = "FriendRequest", Body = "Generic request", LinkUrl = "/Friends" });
            await db.SaveChangesAsync();
        }
        using var owner = Client("alice");
        var response = await owner.PostAsync(Manage + "DeletePersonalData", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = await Token(owner, Manage + "DeletePersonalData"),
            ["Input.ConfirmDeletion"] = "true", ["Input.Password"] = Password
        }));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.False(await db.Users.AnyAsync(x => x.Id == "alice"));
            Assert.False(await db.UserNotifications.AnyAsync(x => x.Type == "FriendStartedWalk"));
            foreach (var row in await db.UserNotifications.Where(x => x.Type != "FriendRequest").ToListAsync())
            {
                Assert.Equal(NotificationPrivacy.NeutralTitle(row.Type), row.Title);
                Assert.Equal(NotificationPrivacy.NeutralBody(row.Type), row.Body);
                Assert.Equal(NotificationPrivacy.Link(row.Type, null), row.LinkUrl); Assert.Null(row.SourceKey);
            }
            Assert.Equal("Generic request", (await db.UserNotifications.SingleAsync(x => x.Type == "FriendRequest")).Body);
        }
        using var friend = Client("bob");
        foreach (var path in new[] { "/Notifications", "/api/notifications", "/api/notifications/center" })
            AssertNoNotificationSecrets(await friend.GetStringAsync(path));
        AssertNoNotificationSecrets(await Download(friend));
    }
}
