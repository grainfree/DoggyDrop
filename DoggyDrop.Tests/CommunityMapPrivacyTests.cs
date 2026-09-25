using System.Text.Json;
using DoggyDrop.Controllers.Api;
using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class CommunityMapPrivacyTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-heatmap-privacy-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData("Active")]
    [InlineData("Completed")]
    public async Task PrivacyZoneWalkPoints_DoNotEnterPublicHeatmap(string status)
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(
            new ApplicationUser { Id = "private", UserName = "private@test" },
            new ApplicationUser { Id = "public", UserName = "public@test" });
        db.PrivacyZones.Add(new PrivacyZone { UserId = "private", Latitude = 46.1, Longitude = 14.1, RadiusMeters = 300 });
        var privateDog = new Dog { Name = "Private dog", OwnerId = "private" };
        var publicDog = new Dog { Name = "Public dog", OwnerId = "public" };
        db.Dogs.AddRange(privateDog, publicDog);
        var privateWalk = new Walk { OwnerId = "private", Dog = privateDog, Status = status };
        var publicWalk = new Walk { OwnerId = "public", Dog = publicDog, Status = status };
        db.Walks.AddRange(privateWalk, publicWalk);
        await db.SaveChangesAsync();
        var now = DateTime.UtcNow;
        db.WalkPoints.AddRange(
            new WalkPoint { WalkId = privateWalk.Id, Latitude = 46.1, Longitude = 14.1, RecordedAt = now },
            new WalkPoint { WalkId = publicWalk.Id, Latitude = 47.1, Longitude = 15.1, RecordedAt = now });
        await db.SaveChangesAsync();

        var response = Assert.IsType<OkObjectResult>(await new CommunityMapApiController(db).Heatmap());
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        var root = json.RootElement;
        var hotspots = root.GetProperty("Hotspots").EnumerateArray().ToArray();
        Assert.DoesNotContain(hotspots, hotspot =>
            hotspot.GetProperty("Latitude").GetDouble() == 46.1 &&
            hotspot.GetProperty("Longitude").GetDouble() == 14.1);
        if (status == "Active")
            Assert.DoesNotContain(hotspots, hotspot => hotspot.GetProperty("Type").GetString() == "active");
        else
            Assert.DoesNotContain(hotspots, hotspot => hotspot.GetProperty("Type").GetString() == "route");
        Assert.Equal(status == "Active" ? 1 : 0, root.GetProperty("ActiveWalkers").GetInt32());
        Assert.Equal(0, root.GetProperty("TrendingRoutes").GetInt32());
    }

    [Fact]
    public async Task RepeatedMovement_RequiresTwoWalkersAndPublishesOnlyStableCellCenter()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var first = await AddWalk(db, "first", "Active");
        var latCenter = (Math.Floor(46.05 / 0.00225) + 0.5) * 0.00225;
        var lngCenter = (Math.Floor(14.51 / 0.00325) + 0.5) * 0.00325;
        var now = DateTime.UtcNow;
        db.WalkPoints.Add(new WalkPoint { WalkId = first.Id, Latitude = latCenter - 0.0002,
            Longitude = lngCenter - 0.0002, RecordedAt = now.AddSeconds(-30) });
        await db.SaveChangesAsync();

        using (var alone = await PublicResponse(db))
        {
            Assert.Equal(1, alone.RootElement.GetProperty("ActiveWalkers").GetInt32());
            Assert.Empty(ActiveHotspots(alone));
        }

        db.WalkPoints.Add(new WalkPoint { WalkId = first.Id, Latitude = latCenter + 0.0001,
            Longitude = lngCenter + 0.0001, RecordedAt = now.AddSeconds(-10) });
        await db.SaveChangesAsync();
        using (var movedAlone = await PublicResponse(db)) Assert.Empty(ActiveHotspots(movedAlone));

        var second = await AddWalk(db, "second", "Active");
        db.WalkPoints.Add(new WalkPoint { WalkId = second.Id, Latitude = latCenter - 0.0001,
            Longitude = lngCenter + 0.0002, RecordedAt = now.AddSeconds(-5) });
        await db.SaveChangesAsync();
        using var together = await PublicResponse(db);
        var hotspot = Assert.Single(ActiveHotspots(together));
        Assert.Equal(2, together.RootElement.GetProperty("ActiveWalkers").GetInt32());
        Assert.Equal(2, hotspot.GetProperty("Count").GetInt32());
        Assert.Equal(latCenter, hotspot.GetProperty("Latitude").GetDouble(), 8);
        Assert.Equal(lngCenter, hotspot.GetProperty("Longitude").GetDouble(), 8);
        Assert.DoesNotContain("first", together.RootElement.ToString());
        Assert.DoesNotContain("second", together.RootElement.ToString());

        db.WalkPoints.Add(new WalkPoint { WalkId = first.Id, Latitude = latCenter + 0.0002,
            Longitude = lngCenter - 0.0001, RecordedAt = now.AddSeconds(-1) });
        await db.SaveChangesAsync();
        using var movedTogether = await PublicResponse(db);
        var repeated = Assert.Single(ActiveHotspots(movedTogether));
        Assert.Equal(hotspot.GetProperty("Latitude").GetDouble(), repeated.GetProperty("Latitude").GetDouble());
        Assert.Equal(hotspot.GetProperty("Longitude").GetDouble(), repeated.GetProperty("Longitude").GetDouble());
    }

    [Fact]
    public async Task SeparateSinglePersonCells_DoNotPublishLocations()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var first = await AddWalk(db, "first", "Active");
        var second = await AddWalk(db, "second", "Active");
        db.WalkPoints.AddRange(
            new WalkPoint { WalkId = first.Id, Latitude = 46.05, Longitude = 14.51, RecordedAt = DateTime.UtcNow.AddMinutes(-1) },
            new WalkPoint { WalkId = second.Id, Latitude = 46.06, Longitude = 14.52, RecordedAt = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        using var response = await PublicResponse(db);
        Assert.Equal(2, response.RootElement.GetProperty("ActiveWalkers").GetInt32());
        Assert.Empty(ActiveHotspots(response));
    }

    [Fact]
    public async Task CompletedRouteHotspot_RequiresTwoOwnersAndKeepsWalkCount()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var first = await AddWalk(db, "first", "Completed");
        var anotherFirst = new Walk { OwnerId = "first", DogId = first.DogId, Status = "Completed" };
        db.Walks.Add(anotherFirst);
        var second = await AddWalk(db, "second", "Completed");
        await db.SaveChangesAsync();
        var latCenter = (Math.Floor(46.05 / 0.00225) + 0.5) * 0.00225;
        var lngCenter = (Math.Floor(14.51 / 0.00325) + 0.5) * 0.00325;
        db.WalkPoints.AddRange(
            new WalkPoint { WalkId = first.Id, Latitude = latCenter - 0.0002,
                Longitude = lngCenter - 0.0001, RecordedAt = DateTime.UtcNow.AddMinutes(-2) },
            new WalkPoint { WalkId = anotherFirst.Id, Latitude = latCenter,
                Longitude = lngCenter, RecordedAt = DateTime.UtcNow.AddMinutes(-1) },
            new WalkPoint { WalkId = second.Id, Latitude = latCenter + 0.0002,
                Longitude = lngCenter + 0.0001, RecordedAt = DateTime.UtcNow.AddMinutes(-1) });
        await db.SaveChangesAsync();
        using var response = await PublicResponse(db);
        var route = Assert.Single(response.RootElement.GetProperty("Hotspots").EnumerateArray(),
            hotspot => hotspot.GetProperty("Type").GetString() == "route");
        Assert.Equal(3, route.GetProperty("Count").GetInt32());
        Assert.Equal("3 sprehodov na tem območju", route.GetProperty("Subtitle").GetString());
        Assert.Equal(latCenter, route.GetProperty("Latitude").GetDouble(), 8);
        Assert.Equal(lngCenter, route.GetProperty("Longitude").GetDouble(), 8);
        Assert.Equal(0, response.RootElement.GetProperty("ActiveWalkers").GetInt32());
        Assert.DoesNotContain("OwnerId", response.RootElement.ToString());
        Assert.DoesNotContain("WalkId", response.RootElement.ToString());
        Assert.DoesNotContain("first", response.RootElement.ToString());
        Assert.DoesNotContain("second", response.RootElement.ToString());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public async Task CompletedWalksFromOneOwner_DoNotPublishRouteHotspot(int walkCount)
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var first = await AddWalk(db, "single-owner", "Completed");
        for (var index = 1; index < walkCount; index++)
            db.Walks.Add(new Walk { OwnerId = first.OwnerId, DogId = first.DogId, Status = "Completed" });
        await db.SaveChangesAsync();

        var walks = await db.Walks.OrderBy(walk => walk.Id).ToListAsync();
        foreach (var walk in walks)
        {
            for (var point = 0; point < 3; point++)
                db.WalkPoints.Add(new WalkPoint { WalkId = walk.Id, Latitude = 46.05,
                    Longitude = 14.51, RecordedAt = DateTime.UtcNow.AddDays(-walk.Id % 3) });
        }
        await db.SaveChangesAsync();

        var response = Assert.IsType<OkObjectResult>(await new CommunityMapApiController(db).Heatmap("week"));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        Assert.DoesNotContain(json.RootElement.GetProperty("Hotspots").EnumerateArray(),
            hotspot => hotspot.GetProperty("Type").GetString() == "route");
        Assert.Equal(0, json.RootElement.GetProperty("TrendingRoutes").GetInt32());
    }

    [Fact]
    public async Task PrivacyZoneOwner_DoesNotSatisfyCompletedRouteContributorMinimum()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var publicWalk = await AddWalk(db, "public-owner", "Completed");
        var privateWalk = await AddWalk(db, "private-owner", "Completed");
        db.PrivacyZones.Add(new PrivacyZone { UserId = privateWalk.OwnerId, Latitude = 46.05,
            Longitude = 14.51, RadiusMeters = 300 });
        db.WalkPoints.AddRange(
            new WalkPoint { WalkId = publicWalk.Id, Latitude = 46.05, Longitude = 14.51, RecordedAt = DateTime.UtcNow },
            new WalkPoint { WalkId = privateWalk.Id, Latitude = 46.05, Longitude = 14.51, RecordedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        using var response = await PublicResponse(db);
        Assert.DoesNotContain(response.RootElement.GetProperty("Hotspots").EnumerateArray(),
            hotspot => hotspot.GetProperty("Type").GetString() == "route");
    }

    [Theory]
    [InlineData("Active", -9)]
    [InlineData("Completed", -1)]
    [InlineData("Interrupted", -1)]
    public async Task StaleOrTerminalWalk_DoesNotContributeToActivePresence(string status, int minutesAgo)
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var walk = await AddWalk(db, "walker", status);
        db.WalkPoints.Add(new WalkPoint { WalkId = walk.Id, Latitude = 46.05, Longitude = 14.51,
            RecordedAt = DateTime.UtcNow.AddMinutes(minutesAgo) });
        await db.SaveChangesAsync();
        using var response = await PublicResponse(db);
        Assert.Equal(0, response.RootElement.GetProperty("ActiveWalkers").GetInt32());
        Assert.Empty(ActiveHotspots(response));
    }

    [Fact]
    public async Task ActiveWalkWithoutPoints_HasNoPublicPresence()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        await AddWalk(db, "walker", "Active");
        using var response = await PublicResponse(db);
        Assert.Equal(0, response.RootElement.GetProperty("ActiveWalkers").GetInt32());
        Assert.Empty(ActiveHotspots(response));
    }

    [Fact]
    public async Task FinishingWalk_RemovesActiveCountOnNextRequest()
    {
        await using var db = Context();
        await db.Database.EnsureCreatedAsync();
        var walk = await AddWalk(db, "walker", "Active");
        db.WalkPoints.Add(new WalkPoint { WalkId = walk.Id, Latitude = 46.05, Longitude = 14.51,
            RecordedAt = DateTime.UtcNow.AddSeconds(-5) });
        await db.SaveChangesAsync();

        using (var before = await PublicResponse(db))
            Assert.Equal(1, before.RootElement.GetProperty("ActiveWalkers").GetInt32());

        walk.Status = "Completed";
        await db.SaveChangesAsync();
        using var after = await PublicResponse(db);
        Assert.Equal(0, after.RootElement.GetProperty("ActiveWalkers").GetInt32());
        Assert.Empty(ActiveHotspots(after));
    }

    private static async Task<Walk> AddWalk(ApplicationDbContext db, string ownerId, string status)
    {
        db.Users.Add(new ApplicationUser { Id = ownerId, UserName = $"{ownerId}@test" });
        var dog = new Dog { Name = "Dog", OwnerId = ownerId };
        var walk = new Walk { OwnerId = ownerId, Dog = dog, Status = status };
        db.Walks.Add(walk);
        await db.SaveChangesAsync();
        return walk;
    }

    private static async Task<JsonDocument> PublicResponse(ApplicationDbContext db)
    {
        var response = Assert.IsType<OkObjectResult>(await new CommunityMapApiController(db).Heatmap());
        return JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
    }

    private static JsonElement[] ActiveHotspots(JsonDocument response) =>
        response.RootElement.GetProperty("Hotspots").EnumerateArray()
            .Where(hotspot => hotspot.GetProperty("Type").GetString() == "active")
            .ToArray();

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False").Options);

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
    }
}
