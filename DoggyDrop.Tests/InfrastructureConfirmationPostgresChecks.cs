using System.Data.Common;
using System.Diagnostics;
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

// Opt-in, fixed loopback PostgreSQL 17; never reads application connection configuration.
public static class InfrastructureConfirmationPostgresChecks
{
    public static async Task<int> RunIsolatedAsync()
    {
        var cs=$"Host=127.0.0.1;Port=59218;Database=epic220_{Guid.NewGuid():N};Username=epic17_review;Pooling=False";
        var options=new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(cs).Options;
        ApplicationDbContext Db()=>new(options);
        await using var db=Db();var count=0;var clock=new InfrastructureConfirmationTests.Clock();
        try {
            var migrations=db.Database.GetMigrations().ToArray();Assert.EndsWith("AddInfrastructureConfirmations",migrations[^1]);
            await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);
            await using(var legacy=new LegacyDb(options)) {
                legacy.Users.Add(new(){Id="owner",UserName="owner"});legacy.Users.Add(new(){Id="other",UserName="other"});
                legacy.DataSources.Add(new(){Id=1,Name="Synthetic OSM",Notes="private"});
                legacy.TrashBins.Add(new(){Id=1,Name="Existing bin",Latitude=46,Longitude=15,IsApproved=true,DataSourceId=1});
                legacy.WaterPoints.Add(new(){Id=1,Name="Existing water",Latitude=46,Longitude=15,IsApproved=true,Potability=WaterPotability.SourceReportedDrinking,DataSourceId=1});
                legacy.Places.Add(new(){Id=1,Name="Existing park",Category=PlaceCategory.DogPark,Latitude=46,Longitude=15,DataSourceId=1});
                legacy.Dogs.Add(new(){Id=1,Name="Dog",OwnerId="owner"});legacy.PlannedWalks.Add(new(){Id=1,OwnerId="owner",Title="Saved plan"});await legacy.SaveChangesAsync();
                legacy.Walks.Add(new(){Id=1,OwnerId="owner",DogId=1,Status="Finished",DistanceMeters=321,PlannedWalkId=1});
                legacy.BinContributions.Add(new(){BinId=1,SubmittedByUserId="owner",Type=BinContributionType.Photo,RequestId=Guid.NewGuid(),BinSnapshot="synthetic"});await legacy.SaveChangesAsync();
                legacy.WalkPoints.Add(new(){WalkId=1,Latitude=46,Longitude=15});await legacy.SaveChangesAsync();
            }
            var before=await Snapshot(cs);await db.GetService<IMigrator>().MigrateAsync(migrations[^1]);Assert.Equal(before,await Snapshot(cs));count++;
            Assert.Empty(await db.InfrastructureConfirmations.ToListAsync());Assert.False(db.Database.HasPendingModelChanges());count++;
            foreach(var water in new[]{false,true}){
                await using var a=Db();await using var b=Db();
                var outcomes=await Task.WhenAll(new InfrastructureConfirmations(a,clock).ConfirmAsync(water,1,"owner",new(46,15,5)),new InfrastructureConfirmations(b,clock).ConfirmAsync(water,1,"owner",new(46,15,5)));
                Assert.Single(outcomes,r=>r.Outcome=="accepted");Assert.Single(outcomes,r=>r.Outcome=="cooldown");count++;
            }
            Assert.Equal(2,await db.InfrastructureConfirmations.CountAsync());
            // Deterministic retirement race: hold the actual infrastructure lock until the competing
            // confirmation has reached it, then commit retirement. No sleep-based ordering.
            foreach(var water in new[]{false,true}){
                await using var edit=Db();await using var tx=await edit.Database.BeginTransactionAsync();
                if(water)await WaterPoints.LockAsync(edit);else await BinCommunityRules.LockAsync(edit);
                var entered=new LockEntered();await using var confirm=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(entered).Options);
                var pending=new InfrastructureConfirmations(confirm,clock).ConfirmAsync(water,1,"other",new(46,15,5));
                await entered.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));Assert.False(pending.IsCompleted);
                if(water)(await edit.WaterPoints.SingleAsync()).IsRetired=true;else (await edit.TrashBins.SingleAsync()).IsRetired=true;
                await edit.SaveChangesAsync();await tx.CommitAsync();Assert.Equal("unavailable",(await pending.WaitAsync(TimeSpan.FromSeconds(15))).Outcome);count++;
            }
            var bin=await db.TrashBins.SingleAsync();var point=await db.WaterPoints.SingleAsync();bin.IsRetired=false;point.IsRetired=false;await db.SaveChangesAsync();
            Assert.Empty(await new InfrastructureTrust(db,clock).BinsAsync());Assert.Empty(await new InfrastructureTrust(db,clock).WaterAsync());Assert.Equal(2,await db.InfrastructureConfirmations.CountAsync());count++;
            foreach(var sql in new[]{"INSERT INTO \"InfrastructureConfirmations\" (\"Type\",\"TrashBinId\",\"WaterPointId\",\"CreatedAt\",\"EvidenceVersion\") VALUES (1,1,1,now(),'00000000-0000-0000-0000-000000000000')",
                "INSERT INTO \"InfrastructureConfirmations\" (\"Type\",\"TrashBinId\",\"CreatedAt\",\"EvidenceVersion\") VALUES (1,999,now(),'00000000-0000-0000-0000-000000000000')",
                "DELETE FROM \"WaterPoints\" WHERE \"Id\"=1"}){await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync(sql));count++;}
            await db.Users.Where(u=>u.Id=="other").ExecuteDeleteAsync();
            await db.Users.Where(u=>u.Id=="owner").ExecuteUpdateAsync(s=>s.SetProperty(u=>u.UserName,"owner"));
            // Dedicated author without other application dependencies tests ON DELETE SET NULL.
            db.Users.Add(new(){Id="deleted"});await db.SaveChangesAsync();await new InfrastructureConfirmations(db,clock).ConfirmAsync(false,1,"deleted",new(46,15,5));
            await db.Users.Where(u=>u.Id=="deleted").ExecuteDeleteAsync();db.ChangeTracker.Clear();
            Assert.Null((await db.InfrastructureConfirmations.OrderByDescending(c=>c.Id).FirstAsync()).UserId);Assert.Equal(0,(await new InfrastructureTrust(db,clock).BinsAsync())[1].RecentUniqueConfirmers);count++;
            await db.Database.ExecuteSqlRawAsync("DELETE FROM \"InfrastructureConfirmations\"");
            // Seed only disposable synthetic rows; defaults preserve all mandatory domain fields.
            for(var id=2;id<=1500;id++){
                db.TrashBins.Add(new(){Id=id,Name="Synthetic bin",Latitude=46,Longitude=15,IsApproved=true});
                db.WaterPoints.Add(new(){Id=id,Latitude=46,Longitude=15,IsApproved=true,Potability=WaterPotability.SourceReportedDrinking});
            }
            await db.SaveChangesAsync();db.ChangeTracker.Clear();
            foreach(var size in new[]{250,500,1500,3000}){
                var history=size==250?10000:50000;var perType=size/2;
                await db.Database.ExecuteSqlRawAsync("DELETE FROM \"InfrastructureConfirmations\"");
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""InfrastructureConfirmations"" (""Type"",""TrashBinId"",""UserId"",""CreatedAt"",""EvidenceVersion"")
                    SELECT 1,b.""Id"",'owner',{clock.Now.UtcDateTime},b.""EvidenceVersion"" FROM generate_series(1,{history/2}) n JOIN ""TrashBins"" b ON b.""Id""=1+(n%{perType})");
                await db.Database.ExecuteSqlInterpolatedAsync($@"INSERT INTO ""InfrastructureConfirmations"" (""Type"",""WaterPointId"",""UserId"",""CreatedAt"",""EvidenceVersion"")
                    SELECT 2,b.""Id"",'owner',{clock.Now.UtcDateTime},b.""EvidenceVersion"" FROM generate_series(1,{history/2}) n JOIN ""WaterPoints"" b ON b.""Id""=1+(n%{perType})");
                var recorder=new QueryRecorder();await using var measured=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(recorder).Options);
                var service=new InfrastructureTrust(measured,clock);var ids=Enumerable.Range(1,perType).ToArray();var watch=Stopwatch.StartNew();
                var bins=await service.BinsAsync(ids);var waters=await service.WaterAsync(ids);watch.Stop();
                Assert.Equal(perType,bins.Count);Assert.Equal(perType,waters.Count);Assert.All(bins.Values.Concat(waters.Values),s=>Assert.Equal(1,s.RecentUniqueConfirmers));
                Assert.Equal(3,recorder.Sql.Count);Assert.Equal(2,recorder.Sql.Count(s=>s.Contains("GROUP BY")&&s.Contains("DISTINCT")));Assert.Empty(measured.ChangeTracker.Entries());
                Console.WriteLine($"Trust performance: infrastructure={size}, history={history}, SQL=3, aggregates={bins.Count+waters.Count}, elapsedMs={watch.Elapsed.TotalMilliseconds:F2}");count++;
            }
            // Down migration removes only this Epic's fields/table; original application rows survive.
            var populated=await Snapshot(cs);await db.GetService<IMigrator>().MigrateAsync(migrations[^2]);Assert.Equal(populated,await Snapshot(cs));count++;
            return count;
        } finally {await db.Database.EnsureDeletedAsync();}
    }
    private sealed class LegacyDb(DbContextOptions<ApplicationDbContext> options):ApplicationDbContext(options){
        protected override void OnModelCreating(ModelBuilder b){base.OnModelCreating(b);b.Ignore<InfrastructureConfirmation>();b.Entity<TrashBin>().Ignore(x=>x.EvidenceVersion);b.Entity<WaterPoint>().Ignore(x=>x.EvidenceVersion);}
    }
    private static async Task<string> Snapshot(string cs){await using var c=new NpgsqlConnection(cs);await c.OpenAsync();var rows=new List<string>();
        foreach(var table in new[]{"AspNetUsers","TrashBins","WaterPoints","Places","DataSources","Dogs","BinContributions","Walks","WalkPoints","PlannedWalks"}){
            await using var q=new NpgsqlCommand($"SELECT coalesce(jsonb_agg(to_jsonb(t)-'EvidenceVersion' ORDER BY to_jsonb(t)::text)::text,'[]') FROM \"{table}\" t",c);rows.Add((string)(await q.ExecuteScalarAsync())!);}return string.Join("\n",rows);}
    private sealed class LockEntered:DbCommandInterceptor{public TaskCompletionSource Reached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<int> r,CancellationToken ct=default){if(c.CommandText.Contains("pg_advisory_xact_lock"))Reached.TrySetResult();return ValueTask.FromResult(r);}}
    private sealed class QueryRecorder:DbCommandInterceptor{public List<string> Sql{get;}=[];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> r,CancellationToken ct=default){Sql.Add(c.CommandText);return ValueTask.FromResult(r);}}
}
