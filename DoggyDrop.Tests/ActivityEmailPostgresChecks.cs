using System.Diagnostics;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace DoggyDrop.Tests;

// Invoked by the isolated local PostgreSQL runner, never by production startup.
public static class ActivityEmailPostgresChecks
{
    public static async Task RunAsync(DbContextOptions<ApplicationDbContext> options,string check)
    {
        await using var guard=new ApplicationDbContext(options);
        if(!guard.Database.IsNpgsql() || !guard.Database.GetDbConnection().DataSource.Contains("127.0.0.1") ||
            !guard.Database.GetDbConnection().Database.StartsWith("epic250_",StringComparison.Ordinal)) throw new InvalidOperationException("Disposable local database required.");
        switch(check) {
            case "atomic": await ActivityEmailTests.CheckAtomic(options);break;
            case "address": await ActivityEmailTests.CheckAddress(options);break;
            case "retries": await ActivityEmailTests.CheckRetries(options);break;
            case "lease": await ActivityEmailTests.CheckLease(options);break;
            case "deletion": await ActivityEmailTests.CheckDeletion(options);break;
            case "export": await ActivityEmailTests.CheckExport(options);break;
            case "workers": await Workers(options);break;
            case "retry-race": await RetryRace(options);break;
            case "preference-race": await PreferenceRace(options);break;
            case "constraints": await Constraints(options);break;
            case "migration": await Migration(options);break;
            default:
                if(check.StartsWith("scale-"))await Scale(options,int.Parse(check[6..]));
                else if(check.StartsWith("review-")){var parts=check.Split('-');await ActivityEmailLifecycleTests.CheckReview(options,int.Parse(parts[1]),bool.Parse(parts[2]));}
                else throw new InvalidOperationException("Unknown check");break;
        }
    }
    private static async Task Workers(DbContextOptions<ApplicationDbContext> options) {
        await using(var seed=await PrivacyTestDatabase.Create(options)){await ActivityEmailTests.Seed(seed.Db);await ActivityEmailTests.Queue(seed.Db);}
        await using var a=new ApplicationDbContext(options);await using var b=new ApplicationDbContext(options);
        var mailA=new ActivityEmailTests.Mail();var mailB=new ActivityEmailTests.Mail();var clock=new ActivityEmailTests.Clock();
        var wa=ActivityEmailTests.Delivery(a,mailA,clock);var wb=ActivityEmailTests.Delivery(b,mailB,clock);
        await Task.WhenAll(wa.RunBatchAsync(),wb.RunBatchAsync());Assert.Equal(1,mailA.Sent.Count+mailB.Sent.Count);
        Assert.Equal(EmailDeliveryStatus.Sent,(await a.NotificationOutbox.SingleAsync()).Status);
    }
    private static async Task RetryRace(DbContextOptions<ApplicationDbContext> options) {
        await using(var seed=await PrivacyTestDatabase.Create(options)){await ActivityEmailTests.Seed(seed.Db);await ActivityEmailTests.Queue(seed.Db);await seed.Db.NotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.Status,EmailDeliveryStatus.Failed).SetProperty(n=>n.AttemptCount,5));}
        await using var a=new ApplicationDbContext(options);await using var b=new ApplicationDbContext(options);var id=await a.NotificationOutbox.Select(n=>n.Id).SingleAsync();
        var results=await Task.WhenAll(NotificationDelivery.RetryAsync(a,id,DateTime.UtcNow),NotificationDelivery.RetryAsync(b,id,DateTime.UtcNow));Assert.Equal(1,results.Sum());Assert.Equal(5,(await a.NotificationOutbox.SingleAsync()).AttemptCount);
    }
    private static async Task PreferenceRace(DbContextOptions<ApplicationDbContext> options) {
        await using(var seed=await PrivacyTestDatabase.Create(options))await ActivityEmailTests.Seed(seed.Db);
        await using var a=new ApplicationDbContext(options);await using var b=new ApplicationDbContext(options);
        static Task<int> Save(ApplicationDbContext db,bool value)=>db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "NotificationPreferences" ("UserId","ContributionUpdates") VALUES ({"a"},{value})
            ON CONFLICT ("UserId") DO UPDATE SET "ContributionUpdates"=EXCLUDED."ContributionUpdates"
            """);
        await Task.WhenAll(Save(a,true),Save(b,false));Assert.Single(await a.NotificationPreferences.ToListAsync());
    }
    private static async Task Constraints(DbContextOptions<ApplicationDbContext> options) {
        await using var f=await PrivacyTestDatabase.Create(options);await ActivityEmailTests.Seed(f.Db);await ActivityEmailTests.Queue(f.Db);
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Db.Database.ExecuteSqlRawAsync("UPDATE \"NotificationOutbox\" SET \"AttemptCount\"=11"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Db.Database.ExecuteSqlRawAsync("UPDATE \"NotificationOutbox\" SET \"Status\"=999"));
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Db.Database.ExecuteSqlRawAsync("UPDATE \"NotificationOutbox\" SET \"RecipientUserId\"='missing'"));
        f.Db.NotificationPreferences.Add(new(){UserId="a"});await f.Db.SaveChangesAsync();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(()=>f.Db.Database.ExecuteSqlRawAsync("INSERT INTO \"NotificationPreferences\" (\"UserId\",\"ContributionUpdates\") VALUES ('a',true)"));
    }
    private static async Task Scale(DbContextOptions<ApplicationDbContext> options,int size) {
        await using var f=await PrivacyTestDatabase.Create(options);await ActivityEmailTests.Seed(f.Db);var now=DateTime.UtcNow;
        f.Db.NotificationOutbox.AddRange(Enumerable.Range(0,size).Select(i=>new NotificationOutbox{RecipientUserId="a",Type=ActivityEmailType.BinApproved,BinId=1,EventKey=$"synthetic-{i}",CreatedAt=now,NextAttemptAt=now}));await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        var mail=new ActivityEmailTests.Mail();var worker=ActivityEmailTests.Delivery(f.Db,mail,new());var sw=Stopwatch.StartNew();var total=0;int batch;
        do {batch=await worker.RunBatchAsync();Assert.InRange(batch,0,NotificationDelivery.BatchSize);total+=batch;}while(batch>0);
        sw.Stop();Assert.Equal(size,total);Assert.Equal(size,mail.Sent.Count);Assert.Equal(size,await f.Db.NotificationOutbox.CountAsync(n=>n.Status==EmailDeliveryStatus.Sent));
        Console.WriteLine(JsonSerializer.Serialize(new{size,elapsedMs=sw.Elapsed.TotalMilliseconds,batchLimit=NotificationDelivery.BatchSize,sent=total}));
    }
    private static async Task Migration(DbContextOptions<ApplicationDbContext> options) {
        await using var db=new ApplicationDbContext(options);var migrations=db.Database.GetMigrations().ToArray();var baseline=migrations[^2];
        await db.GetService<IMigrator>().MigrateAsync(baseline);
        await ActivityEmailTests.Seed(db);
        var source=new DataSource{Name="synthetic source"};db.DataSources.Add(source);await db.SaveChangesAsync();
        db.WaterPoints.Add(new(){Name="water",Latitude=46,Longitude=15,DataSourceId=source.Id});
        db.Places.Add(new(){Name="place",Latitude=46,Longitude=15,Category=PlaceCategory.DogPark,DataSourceId=source.Id});
        var dog=new Dog{Name="synthetic dog",OwnerId="a"};db.Dogs.Add(dog);await db.SaveChangesAsync();
        db.Walks.Add(new(){DogId=dog.Id,OwnerId="a",DistanceMeters=1234});
        db.PlannedWalks.Add(new(){DogId=dog.Id,OwnerId="a",Title="synthetic plan"});
        db.BinContributions.Add(new(){BinId=1,SubmittedByUserId="a",RequestId=Guid.NewGuid(),Type=BinContributionType.Photo});
        db.InfrastructureConfirmations.Add(new(){UserId="a",TrashBinId=1,Type=InfrastructureConfirmationType.TrashBinPresent});
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var tables=new[]{"AspNetUsers","TrashBins","WaterPoints","Places","Dogs","Walks","PlannedWalks","BinContributions","InfrastructureConfirmations","DataSources"};
        // SQL identifiers come exclusively from the fixed table allowlist above; no request/source input.
#pragma warning disable EF1002
        async Task<string[]> Snapshot(){var result=new List<string>();foreach(var table in tables)result.Add(await db.Database.SqlQueryRaw<string>($"SELECT COALESCE(jsonb_agg(to_jsonb(t) ORDER BY t.\"Id\"), '[]'::jsonb)::text AS \"Value\" FROM \"{table}\" t").SingleAsync());return result.ToArray();}
#pragma warning restore EF1002
        var before=await Snapshot();await db.Database.MigrateAsync();Assert.Equal(before,await Snapshot());
        db.NotificationPreferences.Add(new(){UserId="a",ContributionUpdates=false});await db.SaveChangesAsync();Assert.False((await db.NotificationPreferences.SingleAsync()).ContributionUpdates);
        await db.NotificationPreferences.ExecuteDeleteAsync();await ActivityEmailTests.Queue(db);var mail=new ActivityEmailTests.Mail();await ActivityEmailTests.Delivery(db,mail,new()).RunBatchAsync();Assert.Single(mail.Sent);
        await db.GetService<IMigrator>().MigrateAsync(baseline);Assert.Equal(before,await Snapshot());await db.Database.MigrateAsync();Assert.Empty(await db.NotificationOutbox.ToListAsync());
    }
}
