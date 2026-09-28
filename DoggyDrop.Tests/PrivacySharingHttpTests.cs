using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DoggyDrop.Tests;

public sealed partial class PrivacyHttpTests
{
    private async Task<(int Walk, int Photo, int Dog, int Plan)> PrivateIds()
    {
        await using var scope=app.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var walk=await db.Walks.SingleAsync(x=>x.OwnerId=="alice");
        return (walk.Id,await db.WalkPhotos.Select(x=>x.Id).SingleAsync(),walk.DogId,
            await db.PlannedWalks.Select(x=>x.Id).SingleAsync());
    }

    private static void AssertNoPrivateContent(string body)
    {
        foreach(var value in new[] { "walk-image", "Alice dog", "dog-image", "46.5547", "15.6459", "Own stop", "Own plan", "alice@example.invalid" })
            Assert.DoesNotContain(value,WebUtility.HtmlDecode(body),StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null,false)]
    [InlineData("bob",false)]
    [InlineData("bob",true)]
    public async Task DirectPrivateRoutesDenyAnonymousOtherUsersAndFriends(string? user,bool friend)
    {
        var ids=await PrivateIds();
        if(!friend)
        {
            await using var scope=app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Friendships.ExecuteDeleteAsync();
        }
        using var client=Client(user);
        var paths=new[] {
            $"/Walks/Details/{ids.Walk}", $"/Walks/Details/{ids.Walk}?memory=true", $"/Walks/Active/{ids.Walk}",
            $"/Walks/Interrupted/{ids.Walk}", $"/Walks/FinishStatus/{ids.Walk}", $"/Walks/Share/{ids.Walk}",
            $"/Walks/ShareCard/{ids.Walk}", $"/api/walks/{ids.Walk}/photos", $"/api/walks/{ids.Walk}/social",
            $"/Walks/Plan/{ids.Plan}", $"/api/walks/plans/{ids.Plan}", $"/Dogs/Details/{ids.Dog}",
            $"/Dogs/Adventures?dogId={ids.Dog}", $"/Dogs?dogId={ids.Dog}", $"/api/activity/summary?dogId={ids.Dog}", "/Walks/Details/2147483647",
            "/api/walks/2147483647/photos" };
        foreach(var path in paths)
        {
            var response=await client.GetAsync(path);
            Assert.True(response.StatusCode==(user==null?HttpStatusCode.Unauthorized:HttpStatusCode.NotFound),$"{path}: {response.StatusCode}");
            AssertNoPrivateContent(await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task OwnerRetainsHistoryMemoryRoutePhotosPlansAndStatistics()
    {
        var ids=await PrivateIds();
        using var client=Client("alice");
        var details=await client.GetStringAsync($"/Walks/Details/{ids.Walk}");
        Assert.Contains("walk-image",details); Assert.Contains("46.5547",details); Assert.Contains("15.6459",details);
        Assert.Contains("memory",details,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Alice dog",WebUtility.HtmlDecode(details));
        var history=await client.GetStringAsync("/Walks");
        Assert.Contains($"/Walks/Details/{ids.Walk}",history); Assert.Contains("Alice dog",WebUtility.HtmlDecode(history));
        Assert.DoesNotContain("bob-private-dog",history);
        Assert.Contains("walk-image",await client.GetStringAsync($"/api/walks/{ids.Walk}/photos"));
        Assert.Contains("walk-image",await client.GetStringAsync($"/api/walks/{ids.Walk}/social"));
        Assert.Contains("walk-image",await client.GetStringAsync("/api/walks/recent"));
        Assert.Contains("Own plan",await client.GetStringAsync($"/api/walks/plans/{ids.Plan}"));
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync("/api/walks/stats")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/Walks/ShareCard/{ids.Walk}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync($"/Walks/Share/{ids.Walk}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/Dogs/Adventures?dogId={ids.Dog}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await client.GetAsync($"/Dogs/Details/{ids.Dog}")).StatusCode);
    }

    [Theory]
    [InlineData(null)] [InlineData("bob")]
    public async Task PrivateSocialMutationsDoNotAllowIndirectAccess(string? user)
    {
        var ids=await PrivateIds();
        using var client=Client(user);
        var expected=user==null?HttpStatusCode.Unauthorized:HttpStatusCode.NotFound;
        foreach(var path in new[] { $"/api/walks/{ids.Walk}/like", $"/api/walks/photos/{ids.Photo}/like", $"/api/walks/{ids.Walk}/comments" })
        {
            var response=await client.PostAsJsonAsync(path,new { Body="Must not be added", ReactionType="paw" });
            Assert.Equal(expected,response.StatusCode); AssertNoPrivateContent(await response.Content.ReadAsStringAsync());
        }
        var fields=new Dictionary<string,string> { ["id"]=ids.Walk.ToString(), ["photoId"]=ids.Photo.ToString(), ["body"]="Must not be added" };
        if(user!=null) fields["__RequestVerificationToken"]=await Token(client,Manage+"PersonalData");
        foreach(var action in new[] { "ToggleLike", "TogglePhotoReaction", "AddComment", "DeletePhoto", "AddPhoto" })
        {
            var response=await client.PostAsync("/Walks/"+action,new FormUrlEncodedContent(fields));
            Assert.Equal(expected,response.StatusCode); AssertNoPrivateContent(await response.Content.ReadAsStringAsync());
        }
        await using var scope=app.Services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.WalkComments.AnyAsync(x=>x.Body=="Must not be added"));
        Assert.Single(await db.WalkPhotos.ToListAsync());
    }

    [Fact]
    public async Task HistoricalNotificationLinkCannotGrantPrivateWalkAccess()
    {
        var ids=await PrivateIds();
        var link=$"/Walks/Details/{ids.Walk}";
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UserNotifications.Where(x=>x.UserId=="bob").ExecuteUpdateAsync(s=>s.SetProperty(x=>x.LinkUrl,link));
        }
        using var client=Client("bob");
        using var doc=JsonDocument.Parse(await client.GetStringAsync("/api/notifications"));
        var notification=Assert.Single(doc.RootElement.GetProperty("notifications").EnumerateArray());
        Assert.Equal(JsonValueKind.Null,notification.GetProperty("linkUrl").ValueKind);
        var response=await client.GetAsync(link);
        Assert.Equal(HttpStatusCode.NotFound,response.StatusCode);
        AssertNoPrivateContent(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task AnonymousLeaderboardRetainsOnlyUnlinkedWalkAggregates()
    {
        using var client=Client();
        var json=await client.GetStringAsync("/api/leaderboards/local?city=maribor");
        AssertNoPrivateContent(json);
        using var doc=JsonDocument.Parse(json);
        var root=doc.RootElement.GetProperty("board");
        Assert.Empty(root.GetProperty("bestPhotos").EnumerateArray());
        Assert.Empty(root.GetProperty("topDogsThisWeek").EnumerateArray());
        var distance=Assert.Single(root.GetProperty("mostDistance").EnumerateArray());
        Assert.Equal("Uporabnik",distance.GetProperty("label").GetString());
        Assert.Equal(1.234,distance.GetProperty("score").GetDouble(),3);
        Assert.Equal(JsonValueKind.Null,distance.GetProperty("userId").ValueKind);
        Assert.Equal(JsonValueKind.Null,distance.GetProperty("imageUrl").ValueKind);
        Assert.DoesNotContain("/Walks/",json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("recordedAt",json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("startedAt",json,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CommunityFriendsAndOwnProfileContainNoOtherUsersPrivateActivity()
    {
        using var client=Client("bob");
        foreach(var path in new[] { "/Home/Community", "/Friends", "/Home/UserProfile?userId=alice", "/Walks", "/Dogs", "/api/walks/recent", "/api/walks/plans", "/api/walks/stats" })
        {
            var response=await client.GetAsync(path);
            Assert.True(response.StatusCode==HttpStatusCode.OK,$"{path}: {response.StatusCode}");
            var body=await response.Content.ReadAsStringAsync(); AssertNoPrivateContent(body);
            if(path=="/Home/Community")
            {
                Assert.Contains("public-bin-image",body);
                Assert.Contains("Kilometri",body); Assert.Contains("Moji sprehodi",body);
                Assert.DoesNotContain("/Walks/Details/",body); Assert.DoesNotContain("/Walks/TogglePhotoReaction",body);
                Assert.DoesNotContain("Njegovi sprehodi se bodo",body);
            }
            if(path=="/Friends") Assert.DoesNotContain("km v zadnjih",body);
        }
    }

    [Fact]
    public async Task AnonymousMapPreservesCoarseMinimumContributorCellsAndPrivacyZoneExclusion()
    {
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.PrivacyZones.ExecuteDeleteAsync();
            await db.WalkPoints.ExecuteDeleteAsync();
            await db.Walks.ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,"Completed"));
            foreach(var walk in await db.Walks.ToListAsync())
                db.WalkPoints.Add(new WalkPoint { WalkId=walk.Id,Latitude=46.5547,Longitude=15.6459,RecordedAt=DateTime.UtcNow });
            await db.SaveChangesAsync();
        }
        using var client=Client();
        var json=await client.GetStringAsync("/api/community-map/heatmap?range=week");
        AssertNoPrivateContent(json);
        Assert.DoesNotContain("walkId",json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ownerId",json,StringComparison.OrdinalIgnoreCase);
        using(var doc=JsonDocument.Parse(json))
        {
            var hotspots=doc.RootElement.GetProperty("hotspots").EnumerateArray().Where(x=>x.GetProperty("type").GetString()=="route").ToList();
            Assert.Single(hotspots); Assert.Equal(2,hotspots[0].GetProperty("count").GetInt32());
        }
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.PrivacyZones.Add(new PrivacyZone { UserId="alice",Latitude=0,Longitude=0,RadiusMeters=300 });
            await db.SaveChangesAsync();
        }
        using var excluded=JsonDocument.Parse(await client.GetStringAsync("/api/community-map/heatmap?range=week"));
        Assert.DoesNotContain(excluded.RootElement.GetProperty("hotspots").EnumerateArray(),x=>x.GetProperty("type").GetString()=="route");
    }
}
