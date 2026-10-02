using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace DoggyDrop.Tests;

// Explicit local-only harness, matching the established PostgreSQL fixture.
// No application connection configuration or migrations are read/executed.
public static class SmartWalkPostgresChecks
{
    public static async Task<int> RunIsolatedAsync()
    {
        var cs = $"Host=127.0.0.1;Port=59218;Database=epic210_{Guid.NewGuid():N};Username=epic17_review;Pooling=False";
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs).Options;
        await using var db = new ApplicationDbContext(options); var count = 0;
        try
        {
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new() { Id = "synthetic-owner", UserName = "synthetic-owner" });
            db.Places.Add(new() { Name = "Synthetic park", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15 });
            db.WaterPoints.Add(new() { Latitude = 46, Longitude = 15, IsApproved = true, Potability = WaterPotability.SourceReportedDrinking });
            db.TrashBins.Add(new() { Name = "Synthetic bin", Latitude = 46, Longitude = 15, IsApproved = true }); await db.SaveChangesAsync();
            var planner = new SmartWalkPlanner(db, new SmartWalkTests.FakeRoutes());
            Assert.Equal(new[] { "bin", "water", "park" }, (await planner.LoadNearbyAsync(SmartWalkTests.Input(), default)).Select(p => p.Kind)); count++;
            var route = await planner.GenerateAsync(SmartWalkTests.Input(), default); Assert.NotNull(route); Assert.Equal(3, route.Attempts); Assert.Empty(await db.PlannedWalks.ToListAsync()); count++;
            var water = await db.WaterPoints.SingleAsync(); var stop = new SmartWalkPoi(water.Id, "water", "Synthetic water", 46, 15);
            Assert.True(await SmartWalkEligibility.PreviewAvailableAsync(db, [stop], default)); water.IsRetired = true; await db.SaveChangesAsync(); Assert.False(await SmartWalkEligibility.PreviewAvailableAsync(db, [stop], default)); count++;
            water.IsRetired = false; await db.SaveChangesAsync(); Assert.True(await SmartWalkEligibility.PreviewAvailableAsync(db, [stop], default)); count++;
            var plan = SmartWalkEligibility.ToPlan("synthetic-owner", null, route with { Stops = [stop] }); db.PlannedWalks.Add(plan); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            var saved = await db.PlannedWalks.Include(p => p.RoutePoints).Include(p => p.Stops).SingleAsync(); Assert.Equal(route.Route.Points.Count, saved.RoutePoints!.Count); Assert.True(await SmartWalkEligibility.StopsAvailableAsync(db, saved.Stops!.ToArray(), default)); count++;
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await SmartWalkEligibility.LockPlacesAsync(db, [(46, 15)], default);
                await using var other = new ApplicationDbContext(options);
                var exception = await Assert.ThrowsAsync<PostgresException>(() => other.Database.ExecuteSqlRawAsync("SELECT \"Id\" FROM \"Places\" WHERE \"Id\"=1 FOR UPDATE NOWAIT")); Assert.Equal(PostgresErrorCodes.LockNotAvailable, exception.SqlState);
                await transaction.CommitAsync(); count++;
            }
            await db.Places.Where(p => p.Id == 1).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
            Assert.False(await SmartWalkEligibility.PreviewAvailableAsync(db, [new(1, "park", "Park", 46, 15)], default)); count++;
            Assert.Single(await db.TrashBins.ToListAsync()); Assert.Single(await db.WaterPoints.ToListAsync()); Assert.Single(await db.Places.ToListAsync()); Assert.Empty(await db.Walks.ToListAsync()); count++;
            return count;
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
