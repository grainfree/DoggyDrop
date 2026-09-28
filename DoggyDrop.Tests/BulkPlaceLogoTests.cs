using System.Data.Common;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BulkPlaceLogoTests
{
    public static string Url(char value) => $"https://res.cloudinary.com/test/image/upload/v1/doggydrop/places/logos/{new string(value,32)}.webp";
    public static IFormFile Image()
    {
        using var bitmap = new SKBitmap(8,8); bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(SKEncodedImageFormat.Png,100);
        var bytes = data.ToArray();
        return new FormFile(new MemoryStream(bytes),0,bytes.Length,"file","logo.png") { Headers=new HeaderDictionary(),ContentType="image/png" };
    }
    private static BulkPlaceLogos Service(ApplicationDbContext db, Storage storage, IPlaceLogoReferenceReader reader) =>
        new(db,storage,reader,new("test"),NullLogger<BulkPlaceLogos>.Instance);
    private static async Task<PlaceLogoVersion[]> Versions(BulkPlaceLogos service, params int[] ids) =>
        (await service.PreviewAsync(ids)).Select(p=>new PlaceLogoVersion(p.Id,p.UpdatedAtTicks)).ToArray();

    [Fact] public Task ThirtyThreePlacesOneUploadAndOneSave() => CheckShared();
    [Fact] public Task MixedAndOutsideReferencesAreSafe() => CheckCleanup();
    [Fact] public Task FailureAfterSqlInsertRollsBackAssignment() => CheckRollback();
    [Fact] public Task ExceptionAfterCommitRetainsLiveUpload() => CheckAmbiguous();
    [Fact] public Task EditDuringUploadAbortsWholeBatch() => CheckConcurrentEdit();

    internal static async Task CheckShared(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture=await Fixture.Create(33,options);var counter=new Commands();var saves=new Saves();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(fixture.Options).AddInterceptors(counter,saves).Options);
        var storage=new Storage();var references=new Reader(new PlaceLogoReferenceReader(fixture.Options));var service=Service(db,storage,references);
        var versions=await Versions(service,Enumerable.Range(1,33).ToArray());
        var before=await fixture.Db.Places.AsNoTracking().OrderBy(p=>p.Id).ToListAsync();
        counter.Reads=0;
        Assert.Equal(33,await service.ApplyAsync(versions,Image()));Assert.Equal(1,storage.Uploads);Assert.Equal(1,saves.Count);
        Assert.Equal(1,counter.Reads);Assert.Equal(0,references.BatchCalls); // No old logos.
        var after=await fixture.Db.Places.AsNoTracking().OrderBy(p=>p.Id).ToListAsync();
        for(var i=0;i<after.Count;i++)
        {
            var p=after[i];Assert.Equal(storage.NewUrl,p.LogoUrl);Assert.True(p.UpdatedAt>before[i].UpdatedAt);Assert.Equal(0,p.UpdatedAt.Ticks%10);
            Assert.True(FeaturedPlaces.IsCurrent(p,DateTime.UtcNow)==FeaturedPlaces.IsCurrent(before[i],DateTime.UtcNow));
            p.LogoUrl=before[i].LogoUrl;p.UpdatedAt=before[i].UpdatedAt;
            Assert.Equal(JsonSerializer.Serialize(before[i]),JsonSerializer.Serialize(p));
        }
        Assert.All(await fixture.Db.Places.AsNoTracking().ToListAsync(),p=>Assert.NotNull(PlaceCategories.PublicLogo(p.Category,p.LogoUrl,"test")));
        await Assert.ThrowsAsync<BulkPlaceLogoException>(()=>service.ApplyAsync(versions,Image()));Assert.Equal(1,storage.Uploads); // stale/replay: no upload
    }
    internal static async Task CheckCleanup(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f=await Fixture.Create(5,options);var oldX=Url('a');var oldZ=Url('b');
        var rows=await f.Db.Places.OrderBy(p=>p.Id).ToListAsync();rows[0].LogoUrl=oldX;rows[1].LogoUrl=oldX;rows[2].LogoUrl=oldZ;rows[4].LogoUrl=oldX;await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        var storage=new Storage();var reader=new Reader(new PlaceLogoReferenceReader(f.Options));var service=Service(f.Db,storage,reader);
        Assert.Equal(4,await service.ApplyAsync(await Versions(service,1,2,3,4),Image()));
        Assert.Equal(1,storage.Uploads);Assert.Equal(1,reader.BatchCalls);Assert.Equal(2,reader.Last.Count);
        Assert.Equal(oldZ,Assert.Single(storage.Deleted));Assert.Equal(oldX,(await f.Db.Places.AsNoTracking().SingleAsync(p=>p.Id==5)).LogoUrl);
        // Replace the last outside reference: X becomes eligible once, not once per Place.
        storage.Deleted.Clear();await service.ApplyAsync(await Versions(service,5),Image());Assert.Equal(oldX,Assert.Single(storage.Deleted));
    }
    internal static async Task CheckRollback(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f=await Fixture.Create(3,options);await SetOld(f,Url('a'));
        var before=JsonSerializer.Serialize(await f.Db.Places.AsNoTracking().OrderBy(p=>p.Id).ToListAsync());
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(f.Options).AddInterceptors(new FailAfterSave()).Options);
        var storage=new Storage();var service=Service(db,storage,new PlaceLogoReferenceReader(f.Options));
        await Assert.ThrowsAsync<BulkPlaceLogoException>(async()=>await service.ApplyAsync(await Versions(service,1,2,3),Image()));
        Assert.Equal(before,JsonSerializer.Serialize(await f.Db.Places.AsNoTracking().OrderBy(p=>p.Id).ToListAsync()));
        Assert.Equal(storage.NewUrl,Assert.Single(storage.Deleted));Assert.Empty(db.ChangeTracker.Entries());
    }
    internal static async Task CheckAmbiguous(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f=await Fixture.Create(2,options);await SetOld(f,Url('a'));
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(f.Options).AddInterceptors(new FailAfterCommit()).Options);
        var storage=new Storage();var service=Service(db,storage,new PlaceLogoReferenceReader(f.Options));
        await Assert.ThrowsAsync<BulkPlaceLogoException>(async()=>await service.ApplyAsync(await Versions(service,1,2),Image()));
        Assert.All(await f.Db.Places.AsNoTracking().ToListAsync(),p=>Assert.Equal(storage.NewUrl,p.LogoUrl));
        Assert.Empty(storage.Deleted); // Commit succeeded; new live asset AND uncertain old assets retained.
    }
    internal static async Task CheckConcurrentEdit(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f=await Fixture.Create(2,options);await SetOld(f,Url('a'));
        var storage=new Storage();var service=Service(f.Db,storage,new PlaceLogoReferenceReader(f.Options));var versions=await Versions(service,1,2);
        storage.DuringUpload=async()=>
        {
            await using var other=new ApplicationDbContext(f.Options);var winner=await other.Places.SingleAsync(p=>p.Id==2);
            winner.LogoUrl=Url('b');winner.Name="Concurrent edit";winner.UpdatedAt=PlaceUpdates.NextUpdatedAt(winner.UpdatedAt);await other.SaveChangesAsync();
        };
        await Assert.ThrowsAsync<BulkPlaceLogoException>(()=>service.ApplyAsync(versions,Image()));
        var result=await f.Db.Places.AsNoTracking().OrderBy(p=>p.Id).ToListAsync();
        Assert.Equal(Url('a'),result[0].LogoUrl);Assert.Equal(Url('b'),result[1].LogoUrl);Assert.Equal("Concurrent edit",result[1].Name);
        Assert.Equal(storage.NewUrl,Assert.Single(storage.Deleted));
    }

    // The PostgreSQL harness runs this explicitly; no production configuration is consulted.
    internal static async Task CheckOverlapping(DbContextOptions<ApplicationDbContext> options)
    {
        await using var f=await Fixture.Create(3,options);await SetOld(f,Url('a'));
        await using var a=new ApplicationDbContext(options);await using var b=new ApplicationDbContext(options);
        var readyA=new TaskCompletionSource();var readyB=new TaskCompletionSource();var releaseA=new TaskCompletionSource();var releaseB=new TaskCompletionSource();
        var storageA=new Storage { NewUrl=Url('c'),DuringUpload=async()=>{readyA.SetResult();await releaseA.Task.WaitAsync(TimeSpan.FromSeconds(20));} };
        var storageB=new Storage { NewUrl=Url('d'),DuringUpload=async()=>{readyB.SetResult();await releaseB.Task.WaitAsync(TimeSpan.FromSeconds(20));} };
        var serviceA=Service(a,storageA,new PlaceLogoReferenceReader(options));var serviceB=Service(b,storageB,new PlaceLogoReferenceReader(options));
        var va=await Versions(serviceA,1,2);var vb=await Versions(serviceB,2,3);
        var pendingA=serviceA.ApplyAsync(va,Image());var pendingB=serviceB.ApplyAsync(vb,Image());
        try
        {
            await Task.WhenAll(readyA.Task,readyB.Task).WaitAsync(TimeSpan.FromSeconds(15));releaseA.TrySetResult();Assert.Equal(2,await pendingA);
            releaseB.TrySetResult();await Assert.ThrowsAsync<BulkPlaceLogoException>(()=>pendingB);
        }
        finally {releaseA.TrySetResult();releaseB.TrySetResult();}
        var rows=await f.Db.Places.AsNoTracking().OrderBy(p=>p.Id).ToListAsync();Assert.Equal(Url('c'),rows[0].LogoUrl);Assert.Equal(Url('c'),rows[1].LogoUrl);Assert.Equal(Url('a'),rows[2].LogoUrl);
        Assert.Empty(storageA.Deleted);Assert.Equal(Url('d'),Assert.Single(storageB.Deleted));
    }

    [Theory]
    [InlineData(PlaceCategory.Veterinarian,true)] [InlineData(PlaceCategory.PetShop,true)] [InlineData(PlaceCategory.Groomer,true)]
    [InlineData(PlaceCategory.DogSchool,true)] [InlineData(PlaceCategory.DogFriendlyCafe,true)]
    [InlineData(PlaceCategory.DogPark,false)] [InlineData(PlaceCategory.DogBeach,false)] [InlineData((PlaceCategory)999,false)]
    public async Task CategoryPolicyRejectsWholeMixedSelection(PlaceCategory category,bool allowed)
    {
        await using var f=await Fixture.Create(2);var p=await f.Db.Places.FindAsync(2);p!.Category=category;await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        var storage=new Storage();var service=Service(f.Db,storage,new PlaceLogoReferenceReader(f.Options));
        if(allowed)Assert.Equal(2,await service.ApplyAsync(await Versions(service,1,2),Image()));
        else {await Assert.ThrowsAsync<BulkPlaceLogoException>(()=>service.PreviewAsync([1,2]));Assert.Equal(0,storage.Uploads);}
    }
    [Fact]
    public async Task SelectionIsBoundedDeduplicatedAndMissingRecordsRejected()
    {
        await using var f=await Fixture.Create(100);var storage=new Storage();var service=Service(f.Db,storage,new PlaceLogoReferenceReader(f.Options));
        Assert.Single(await service.PreviewAsync([1,1]));Assert.Equal(100,(await service.PreviewAsync(Enumerable.Range(1,100).ToArray())).Count);
        foreach(var ids in new int[]?[]{null,[],[0],[-1],[101],Enumerable.Repeat(1,101).ToArray()})
            await Assert.ThrowsAsync<BulkPlaceLogoException>(()=>service.PreviewAsync(ids));
        var versions=await Versions(service,1);await f.Db.Places.Where(p=>p.Id==1).ExecuteDeleteAsync();
        await Assert.ThrowsAsync<BulkPlaceLogoException>(()=>service.ApplyAsync(versions,Image()));Assert.Equal(0,storage.Uploads);
    }
    [Fact]
    public async Task MaximumSelectionStillUsesOneUploadAndOneBatchReferenceCheck()
    {
        await using var f=await Fixture.Create(100);await SetOld(f,Url('a'));var storage=new Storage();var reader=new Reader(new PlaceLogoReferenceReader(f.Options));
        var service=Service(f.Db,storage,reader);
        Assert.Equal(100,await service.ApplyAsync(await Versions(service,Enumerable.Range(1,100).ToArray()),Image()));
        Assert.Equal(1,storage.Uploads);Assert.Equal(1,reader.BatchCalls);Assert.Equal(Url('a'),Assert.Single(storage.Deleted));
        Assert.Equal(100,await f.Db.Places.CountAsync(p=>p.LogoUrl==storage.NewUrl));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task UploadRejectionOrExceptionChangesNothing(bool throws)
    {
        await using var f=await Fixture.Create(2);await SetOld(f,Url('a'));var storage=new Storage { Reject=!throws,Throws=throws };
        var service=Service(f.Db,storage,new PlaceLogoReferenceReader(f.Options));
        await Assert.ThrowsAsync<BulkPlaceLogoException>(async()=>await service.ApplyAsync(await Versions(service,1,2),Image()));
        Assert.All(await f.Db.Places.AsNoTracking().ToListAsync(),p=>Assert.Equal(Url('a'),p.LogoUrl));Assert.Empty(storage.Deleted);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CleanupFailureOrUnknownReferencesDoesNotUndoSuccess(bool referenceFailure)
    {
        await using var f=await Fixture.Create(2);await SetOld(f,Url('a'));var storage=new Storage { DeleteFails=!referenceFailure };
        var reader=new Reader(new PlaceLogoReferenceReader(f.Options)){Throws=referenceFailure};var service=Service(f.Db,storage,reader);
        Assert.Equal(2,await service.ApplyAsync(await Versions(service,1,2),Image()));
        Assert.All(await f.Db.Places.AsNoTracking().ToListAsync(),p=>Assert.Equal(storage.NewUrl,p.LogoUrl));
        if(referenceFailure)Assert.Empty(storage.Deleted);else Assert.Equal(Url('a'),Assert.Single(storage.Deleted));
    }
    [Fact]
    public async Task FailedSaveAndFailedReferenceCheckRetainsPossibleOrphan()
    {
        await using var f=await Fixture.Create(1);
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(f.Options).AddInterceptors(new FailAfterSave()).Options);
        var storage=new Storage();var service=Service(db,storage,new Reader(new PlaceLogoReferenceReader(f.Options)){Throws=true});
        await Assert.ThrowsAsync<BulkPlaceLogoException>(async()=>await service.ApplyAsync(await Versions(service,1),Image()));Assert.Empty(storage.Deleted);
    }
    [Theory]
    [InlineData("https://external.example/logo.webp")] [InlineData("/uploads/../logo.png")]
    [InlineData("https://res.cloudinary.com/other/image/upload/v1/doggydrop/places/logos/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp")]
    public async Task ArbitraryExternalOtherCloudAndLocalAssetsNeverReachDeletion(string old)
    {
        await using var f=await Fixture.Create(1);await SetOld(f,old);var storage=new Storage();var reader=new Reader(new PlaceLogoReferenceReader(f.Options));
        var service=Service(f.Db,storage,reader);await service.ApplyAsync(await Versions(service,1),Image());Assert.Empty(storage.Deleted);Assert.Equal(0,reader.BatchCalls);
    }
    private static async Task SetOld(Fixture f,string old)
    {foreach(var p in await f.Db.Places.ToListAsync())p.LogoUrl=old;await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();}

    public static string Alias(string kind, char id = 'a') => kind switch
    {
        "version" => Url(id).Replace("/v1/", "/v456/"),
        "host" => Url(id).Replace("res.cloudinary.com", "RES.CLOUDINARY.COM"),
        "transform" => PlaceLogoDelivery.ForMarker(Url(id), "test")!,
        "safe-transform" => Url(id).Replace("/upload/", "/upload/f_auto,q_auto/"),
        "query" => Url(id) + "?cache=1",
        "fragment" => Url(id) + "#preview",
        _ => throw new ArgumentException(nameof(kind))
    };

    [Theory]
    [InlineData("version")] [InlineData("host")] [InlineData("transform")]
    [InlineData("query")] [InlineData("fragment")] [InlineData("safe-transform")]
    public Task OutsideAliasProtectsLiveAsset(string kind) => CheckAlias(kind);

    internal static async Task CheckAlias(string kind, DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f = await Fixture.Create(2, options);
        var rows = await f.Db.Places.OrderBy(p => p.Id).ToListAsync();
        rows[0].LogoUrl = Url('a'); rows[1].LogoUrl = Alias(kind);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var storage = new Storage(); var reader = new Reader(new PlaceLogoReferenceReader(f.Options));
        var service = Service(f.Db, storage, reader);
        Assert.Equal(1, await service.ApplyAsync(await Versions(service, 1), Image()));
        Assert.Empty(storage.Deleted); Assert.Equal(1, reader.BatchCalls);
        Assert.Equal(Alias(kind), (await f.Db.Places.AsNoTracking().SingleAsync(p => p.Id == 2)).LogoUrl);
    }

    [Fact] public Task MixedAliasesDeleteOnceOnlyAfterLastReferencesReplaced() => CheckMixedAliases();
    internal static async Task CheckMixedAliases(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f = await Fixture.Create(4, options);
        var rows = await f.Db.Places.OrderBy(p => p.Id).ToListAsync();
        var aliases = new[] { "version", "host", "transform", "query" };
        for (var i = 0; i < rows.Count; i++) rows[i].LogoUrl = Alias(aliases[i]);
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var storage = new Storage(); var reader = new Reader(new PlaceLogoReferenceReader(f.Options));
        var service = Service(f.Db, storage, reader);
        await service.ApplyAsync(await Versions(service, 1), Image()); Assert.Empty(storage.Deleted);
        await service.ApplyAsync(await Versions(service, 2, 3, 4), Image());
        Assert.True(PlaceLogoDelivery.TryManagedAsset(Assert.Single(storage.Deleted), "test", out var deleted));
        Assert.Equal("doggydrop/places/logos/" + new string('a', 32), deleted.PublicId);
        Assert.Single(reader.Last); Assert.Equal(2, reader.BatchCalls); Assert.Equal(2, storage.Uploads);
    }

    [Fact]
    public async Task DifferentNewUrlWithSameIdentityIsExcludedBeforeCleanup()
    {
        await using var f = await Fixture.Create(2); await SetOld(f, Url('a'));
        var storage = new Storage { NewUrl = Alias("version") };
        var reader = new Reader(new PlaceLogoReferenceReader(f.Options)); var service = Service(f.Db, storage, reader);
        await service.ApplyAsync(await Versions(service, 1, 2), Image());
        Assert.Empty(storage.Deleted); Assert.Equal(0, reader.BatchCalls);
        Assert.All(await f.Db.Places.AsNoTracking().ToListAsync(), p => Assert.Equal(storage.NewUrl, p.LogoUrl));
    }

    [Fact] public Task LosingBulkRetainsWinningUploadReferencedThroughAlias() => CheckAliasWinner();
    internal static async Task CheckAliasWinner(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f = await Fixture.Create(2, options); await SetOld(f, Url('a'));
        var storage = new Storage(); var service = Service(f.Db, storage, new PlaceLogoReferenceReader(f.Options));
        var versions = await Versions(service, 1, 2);
        storage.DuringUpload = async () =>
        {
            await using var other = new ApplicationDbContext(f.Options);
            var winner = await other.Places.SingleAsync(p => p.Id == 2);
            winner.LogoUrl = Alias("transform", 'c'); winner.UpdatedAt = PlaceUpdates.NextUpdatedAt(winner.UpdatedAt);
            await other.SaveChangesAsync();
        };
        await Assert.ThrowsAsync<BulkPlaceLogoException>(() => service.ApplyAsync(versions, Image()));
        Assert.Empty(storage.Deleted);
        Assert.Equal(Alias("transform", 'c'), (await f.Db.Places.AsNoTracking().SingleAsync(p => p.Id == 2)).LogoUrl);
    }

    [Fact]
    public async Task UnresolvedCloudinaryReferenceRetainsCandidatesAfterSuccess()
    {
        await using var f = await Fixture.Create(2); await SetOld(f, Url('a'));
        var outside = await f.Db.Places.FindAsync(2);
        outside!.LogoUrl = Url('a').Replace("/upload/", "/upload/unknown_transform/");
        await f.Db.SaveChangesAsync(); f.Db.ChangeTracker.Clear();
        var storage = new Storage(); var service = Service(f.Db, storage, new PlaceLogoReferenceReader(f.Options));
        Assert.Equal(1, await service.ApplyAsync(await Versions(service, 1), Image())); Assert.Empty(storage.Deleted);
    }

    [Fact] public Task AmbiguousCommitRetainsUploadEvenAfterReferenceBecomesAlias() => CheckAmbiguousAlias();
    internal static async Task CheckAmbiguousAlias(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var f = await Fixture.Create(2, options); await SetOld(f, Url('a'));
        var afterCommit = new FailAfterCommit(async () =>
        {
            await using var other = new ApplicationDbContext(f.Options);
            foreach (var row in await other.Places.ToListAsync())
            {
                row.LogoUrl = Alias("query", 'c'); row.UpdatedAt = PlaceUpdates.NextUpdatedAt(row.UpdatedAt);
            }
            await other.SaveChangesAsync();
        });
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(f.Options).AddInterceptors(afterCommit).Options);
        var storage = new Storage(); var service = Service(db, storage, new PlaceLogoReferenceReader(f.Options));
        await Assert.ThrowsAsync<BulkPlaceLogoException>(async () => await service.ApplyAsync(await Versions(service, 1, 2), Image()));
        Assert.Empty(storage.Deleted);
        Assert.All(await f.Db.Places.AsNoTracking().ToListAsync(), p => Assert.Equal(Alias("query", 'c'), p.LogoUrl));
    }
    private sealed class Storage:IPlaceLogoStorage
    {
        public string NewUrl=Url('c');public int Uploads;public bool Reject,Throws,DeleteFails;public Func<Task>? DuringUpload;public List<string> Deleted=[];
        public async Task<string?> UploadAsync(IFormFile file){Uploads++;if(DuringUpload!=null)await DuringUpload();if(Throws)throw new IOException("Upload failure");return Reject?null:NewUrl;}
        public Task DeleteManagedAsync(string? url){Deleted.Add(url!);if(DeleteFails)throw new IOException("Cleanup failure");return Task.CompletedTask;}
    }
    private sealed class Reader(IPlaceLogoReferenceReader inner):IPlaceLogoReferenceReader
    {
        public int BatchCalls;public bool Throws;public IReadOnlyCollection<string> Last=[];
        public Task<bool> IsReferencedAsync(string url)=>inner.IsReferencedAsync(url);
        public Task<IReadOnlySet<PlaceLogoAsset>> FindReferencedAsync(IReadOnlyCollection<string> urls){BatchCalls++;Last=urls;if(Throws)throw new IOException("Reference unavailable");return inner.FindReferencedAsync(urls);}
    }
    private sealed class FailAfterSave:SaveChangesInterceptor
    {public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e,int result,CancellationToken ct=default)=>throw new IOException("Injected failure after SQL");}
    private sealed class FailAfterCommit(Func<Task>? afterCommit = null):DbTransactionInterceptor
    {public override async Task TransactionCommittedAsync(DbTransaction t,TransactionEndEventData e,CancellationToken ct=default){if(afterCommit!=null)await afterCommit();throw new IOException("Commit outcome unknown to caller");}}
    private sealed class Saves:SaveChangesInterceptor
    {public int Count;public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> r,CancellationToken ct=default){Count++;return ValueTask.FromResult(r);}}
    private sealed class Commands:DbCommandInterceptor
    {public int Reads;public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> r,CancellationToken ct=default){if(c.CommandText.TrimStart().StartsWith("SELECT"))Reads++;return ValueTask.FromResult(r);}}
    private sealed class Fixture:IAsyncDisposable
    {
        public required ApplicationDbContext Db;public required DbContextOptions<ApplicationDbContext> Options;private string? path;
        public static async Task<Fixture> Create(int count,DbContextOptions<ApplicationDbContext>? options=null)
        {
            var path=options==null?Path.Combine(Path.GetTempPath(),$"bulk-logo-{Guid.NewGuid():N}.db"):null;
            options??=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            var db=new ApplicationDbContext(options);await db.Database.EnsureCreatedAsync();
            db.Places.AddRange(Enumerable.Range(1,count).Select(i=>new Place{Id=i,Name="Branch "+i,Category=PlaceCategory.PetShop,Latitude=46,Longitude=15,CreatedAt=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc),UpdatedAt=new DateTime(2026,1,1,0,0,0,DateTimeKind.Utc)}));
            await db.SaveChangesAsync();db.ChangeTracker.Clear();return new(){Db=db,Options=options,path=path};
        }
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();if(path!=null)File.Delete(path);}
    }
}
