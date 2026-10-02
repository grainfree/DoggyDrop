using System.Data.Common;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;
namespace DoggyDrop.Tests;

// Explicit opt-in runner only. Fixed loopback test cluster, UUID database; no application configuration.
public static class WaterPointPostgresChecks
{
    public static async Task<int> RunIsolatedAsync()
    {
        var cs=$"Host=127.0.0.1;Port=59218;Database=epic202_{Guid.NewGuid():N};Username=epic17_review;Pooling=False";
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs).Options;
        ApplicationDbContext Db()=>new(options);
        await using var db=Db();var passed=0;
        try{
            var migrations=db.Database.GetMigrations().ToArray();Assert.EndsWith("AddWaterPoints",migrations[^1]);
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);passed++;
            db.Users.Add(new(){Id="water-test-user",UserName="water-test-user"});
            db.DataSources.Add(new(){Name="Synthetic OSM",Type=DataSourceType.PublicDataset});await db.SaveChangesAsync();
            db.TrashBins.Add(new(){Name="Existing bin",Latitude=46,Longitude=15,IsApproved=true,DataSourceId=1});
            db.Places.Add(new(){Name="Existing park",Category=PlaceCategory.DogPark,Latitude=47,Longitude=15,DataSourceId=1});
            db.Dogs.Add(new(){Name="Dog",OwnerId="water-test-user"});await db.SaveChangesAsync();
            db.Walks.Add(new(){OwnerId="water-test-user",DogId=1,Status="Finished",DistanceMeters=321,EndedAt=DateTime.UtcNow});
            db.BinContributions.Add(new(){BinId=1,SubmittedByUserId="water-test-user",Type=BinContributionType.Issue,Reason=BinIssueReason.DAMAGED,RequestId=Guid.NewGuid(),BinSnapshot="synthetic"});await db.SaveChangesAsync();
            db.WalkPoints.Add(new(){WalkId=1,Latitude=46,Longitude=15,RecordedAt=DateTime.UtcNow});await db.SaveChangesAsync();db.ChangeTracker.Clear();
            var before=await Snapshot(cs);await db.GetService<IMigrator>().MigrateAsync(migrations[^1]);var after=await Snapshot(cs);
            Assert.Equal(before,after);Assert.Empty(await db.WaterPoints.ToListAsync());passed++;
            await using(var conn=new NpgsqlConnection(cs)){await conn.OpenAsync();await using var cmd=new NpgsqlCommand("SELECT count(*) FROM pg_indexes WHERE tablename='WaterPoints'",conn);Assert.Equal(4L,(long)(await cmd.ExecuteScalarAsync())!);}passed++;
            await new WaterImportService(db,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row()]);var point=await db.WaterPoints.SingleAsync();Assert.Single(await WaterPoints.LoadAsync(db.WaterPoints));passed++;
            point.IsRetired=true;point.UpdatedAt=PlaceUpdates.NextUpdatedAt(point.UpdatedAt);await db.SaveChangesAsync();Assert.Empty(await WaterPoints.LoadAsync(db.WaterPoints));
            await Assert.ThrowsAsync<BinImportException>(()=>new WaterImportService(db,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row()]));passed++;
            point=await db.WaterPoints.SingleAsync();point.IsRetired=false;point.UpdatedAt=PlaceUpdates.NextUpdatedAt(point.UpdatedAt);await db.SaveChangesAsync();Assert.Equal(1,point.DataSourceId);Assert.Single(await WaterPoints.LoadAsync(db.WaterPoints));passed++;
            await using(var a=Db())await using(var b=Db()){var first=await a.WaterPoints.SingleAsync();var stale=await b.WaterPoints.SingleAsync();first.Name="new";first.UpdatedAt=PlaceUpdates.NextUpdatedAt(first.UpdatedAt);await a.SaveChangesAsync();stale.Name="stale";await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>b.SaveChangesAsync());}passed++;
            foreach(var sql in new[]{"UPDATE \"WaterPoints\" SET \"Latitude\"=91", "UPDATE \"WaterPoints\" SET \"Latitude\"='NaN'::float8", "UPDATE \"WaterPoints\" SET \"Potability\"=2", "UPDATE \"WaterPoints\" SET \"Access\"=3", "UPDATE \"WaterPoints\" SET \"DogAccess\"=99", "UPDATE \"WaterPoints\" SET \"DataSourceId\"=99999"}){
                await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql));passed++;}
            var pause=new PauseSave();var entered=new LockEntered();
            await using(var first=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(pause).Options))
            await using(var second=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(entered).Options)){
                var pending=new WaterImportService(first,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row(lat:48)]);Task<int>? overlap=null;
                try{await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));overlap=new WaterImportService(second,TimeProvider.System).ImportAsync(1,[WaterPointTests.Row(lat:48)]);await entered.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));Assert.False(overlap.IsCompleted);}finally{pause.Release.TrySetResult();}
                Assert.Equal(1,await pending.WaitAsync(TimeSpan.FromSeconds(15)));await Assert.ThrowsAsync<BinImportException>(()=>overlap!.WaitAsync(TimeSpan.FromSeconds(15)));
            }passed++;
            await db.DataSources.Where(s=>s.Id==1).ExecuteDeleteAsync();db.ChangeTracker.Clear();Assert.All(await db.WaterPoints.ToListAsync(),p=>Assert.Null(p.DataSourceId));passed++;
            Assert.Equal(1,await db.TrashBins.CountAsync());Assert.Equal(1,await db.Places.CountAsync());Assert.Equal(1,await db.BinContributions.CountAsync());Assert.Equal(1,await db.WalkPoints.CountAsync());passed++;
            return passed;
        }finally{await db.Database.EnsureDeletedAsync();}
    }
    private static async Task<string> Snapshot(string cs){
        await using var c=new NpgsqlConnection(cs);await c.OpenAsync();var result=new List<string>();
        foreach(var table in new[]{"AspNetUsers","DataSources","TrashBins","Places","Dogs","Walks","WalkPoints","BinContributions"}){
            await using var q=new NpgsqlCommand($"SELECT coalesce(jsonb_agg(to_jsonb(t) ORDER BY to_jsonb(t)::text)::text,'[]') FROM \"{table}\" t",c);result.Add((string)(await q.ExecuteScalarAsync())!);}
        return string.Join("\n",result);
    }
    private sealed class PauseSave:SaveChangesInterceptor{
        public TaskCompletionSource Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> r,CancellationToken ct=default){Reached.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(30),ct);return r;}
    }
    private sealed class LockEntered:DbCommandInterceptor{
        public TaskCompletionSource Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<int> r,CancellationToken ct=default){if(c.CommandText.Contains("pg_advisory_xact_lock(194721, 202)"))Reached.TrySetResult();return ValueTask.FromResult(r);}
    }
}
