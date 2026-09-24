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
        Assert.Contains(hotspots, hotspot =>
            hotspot.GetProperty("Latitude").GetDouble() == 47.1 &&
            hotspot.GetProperty("Longitude").GetDouble() == 15.1);
        Assert.Equal(status == "Active" ? 1 : 0, root.GetProperty("ActiveWalkers").GetInt32());
        Assert.Equal(status == "Completed" ? 1 : 0, root.GetProperty("TrendingRoutes").GetInt32());
    }

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False").Options);

    public void Dispose()
    {
        if (File.Exists(_db)) File.Delete(_db);
    }
}
