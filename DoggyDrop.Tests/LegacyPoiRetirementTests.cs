using System.Text.Json;
using DoggyDrop.Controllers.Api;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class LegacyPoiRetirementTests
{
    [Fact]
    public async Task PlannerSourcesAreEligibleNearbyPersistedPlacesOnly()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Places.AddRange(
            new Place { Name = "Current park", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15 },
            new Place { Name = "Current cafe", Category = PlaceCategory.DogFriendlyCafe, Latitude = 46.001, Longitude = 15 },
            new Place { Name = "Current shop", Category = PlaceCategory.PetShop, Latitude = 46.002, Longitude = 15 },
            new Place { Name = "Inactive", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15, IsActive = false },
            new Place { Name = "Distant", Category = PlaceCategory.DogPark, Latitude = 47, Longitude = 15 },
            new Place { Name = "Beach is not water", Category = PlaceCategory.DogBeach, Latitude = 46, Longitude = 15 },
            new Place { Name = "Unsupported", Category = (PlaceCategory)99, Latitude = 46, Longitude = 15 },
            new Place { Name = "Bad\nname", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15 });
        await db.SaveChangesAsync();
        var places = await PlannerPlaceSource.LoadAsync(db, 46, 15, 2);
        Assert.Equal(new[] { "Current park", "Current cafe", "Current shop" }, places.Select(p => p.Name));
        Assert.Equal(new[] { "park", "cafe", "shop" }, places.Select(p => p.Type));
        Assert.All(places, p => Assert.InRange(p.Priority, 0, 2000));
        Assert.Empty(await PlannerPlaceSource.LoadAsync(db, 45, 13, 2));
        Assert.Equal(8, await db.Places.CountAsync());
    }

    [Fact]
    public async Task HistoricalCatalogVisitsStillResolveReconstructAndStayOutOfPublicHeatmap()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        db.Users.Add(new ApplicationUser { Id = "history", UserName = "history" });
        var dog = new Dog { OwnerId = "history", Name = "Luna" }; db.Dogs.Add(dog); await db.SaveChangesAsync();
        var start = DateTime.UtcNow.AddMinutes(-10);
        foreach (var park in ParkLocationCatalog.All.Take(5))
            db.DogParkVisits.Add(new DogParkVisit { DogId = dog.Id, UserId = "history", PlaceKey = park.PlaceKey,
                ParkName = park.Name, Area = park.Area, Address = park.Address, Latitude = park.Latitude, Longitude = park.Longitude, VisitedAt = start });
        db.DogParkVisits.Add(new DogParkVisit { DogId = dog.Id, UserId = "history", PlaceKey = "obsolete-key", ParkName = "Historical label", VisitedAt = start });
        db.UserXpEvents.Add(new UserXpEvent { UserId = "history", ActivityType = "VisitNewPark", XpAmount = 40, ReferenceType = "DogParkVisit", ReferenceId = ParkLocationCatalog.All[0].PlaceKey });
        await db.SaveChangesAsync();
        var rows = await db.DogParkVisits.AsNoTracking().ToListAsync();
        Assert.Equal(24, ParkLocationCatalog.All.Count);
        Assert.Equal(rows[0].ParkName, ParkLocationCatalog.Find(rows[0].PlaceKey)!.Name);
        Assert.Null(ParkLocationCatalog.Find("obsolete-key"));
        var stamps = new MapStampService().BuildCollection(rows);
        Assert.Equal(6, stamps.TotalStamps);
        Assert.Contains(stamps.Stamps, s => s.Name == "Historical label");
        var achievements = new UserAchievementService(db, new NotificationService(db));
        await achievements.ReconcileUserAsync("history");
        var earned = await db.UserAchievements.SingleAsync(a => a.AchievementKey == UserAchievementCatalog.Explorer5Places);
        Assert.Equal(start, earned.UnlockedAt);
        await achievements.ReconcileUserAsync("history");
        Assert.Single(await db.UserAchievements.Where(a => a.AchievementKey == UserAchievementCatalog.Explorer5Places).ToListAsync());
        Assert.Single(await db.UserXpEvents.ToListAsync());
        var response = Assert.IsType<OkObjectResult>(await new CommunityMapApiController(db).Heatmap("week"));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(response.Value));
        Assert.Empty(json.RootElement.GetProperty("Hotspots").EnumerateArray());
        Assert.Equal(0, json.RootElement.GetProperty("PopularParks").GetInt32());
        Assert.Equal(6, await db.DogParkVisits.CountAsync());
    }
}
