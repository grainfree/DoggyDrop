using System.Data.Common;
using System.Text;
using System.Text.Json;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PlaceImportTests
{
    internal static PlaceImportRow Row(int number = 2, string name = "Test shop", double lat = 46, double lon = 15) =>
        new(number, name, PlaceCategory.PetShop, lat, lon, "Glavna 1", "+386 1 234 567", "https://example.org", "Opis", null, ImportRowStatus.Ready, []);
    internal static Task<ImportCsv> Csv(string value, string? delimiter = ";") =>
        BinImportCsv.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(value)), delimiter);

    [Theory]
    [InlineData("Veterinarian", PlaceCategory.Veterinarian)] [InlineData("PetShop", PlaceCategory.PetShop)]
    [InlineData("Groomer", PlaceCategory.Groomer)] [InlineData("DogSchool", PlaceCategory.DogSchool)]
    [InlineData("DogFriendlyCafe", PlaceCategory.DogFriendlyCafe)] [InlineData("DogPark", PlaceCategory.DogPark)]
    [InlineData("DogBeach", PlaceCategory.DogBeach)] [InlineData(" pet-shop ", PlaceCategory.PetShop)]
    public async Task MapsAllCategoriesAndOnlyExplicitAliases(string category, PlaceCategory expected)
    {
        var csv = await Csv($"Name;Latitude;Longitude;Category\nShop;46;15;{category}");
        var row = Assert.Single(PlaceImportMappingRules.Map(csv, PlaceImportMapping.Suggest(csv.Headers), null));
        Assert.Equal(expected, row.Category); Assert.Equal(ImportRowStatus.Ready, row.Status);
    }

    [Fact]
    public async Task RepresentativeStoreCsvUsesFixedCategoryAndIgnoresHelperColumns()
    {
        var csv = await Csv("\uFEFFName;Category;Address;Latitude;Longitude;Phone;Website;CoordinateStatus;IsActive;Source;SourceUrl\nMr.Pet Test;UNKNOWN;Glavna 1;46;15;+386 123;https://example.org;unverified;false;IGNORED;https://ignored.invalid");
        var mapping = PlaceImportMapping.Suggest(csv.Headers);
        Assert.Equal(new PlaceImportMapping(0, 3, 4, 1, 2, 5, 6), mapping);
        var row = Assert.Single(PlaceImportMappingRules.Map(csv, mapping with { Category = 999 }, PlaceCategory.PetShop));
        Assert.Equal(ImportRowStatus.Ready, row.Status); Assert.Equal(PlaceCategory.PetShop, row.Category);
        Assert.Equal("Mr.Pet Test", row.Name); Assert.Equal("+386 123", row.Phone); Assert.Equal("https://example.org", row.Website);
        Assert.DoesNotContain("IGNORED", JsonSerializer.Serialize(row));
        Assert.Equal(ImportRowStatus.Invalid, Assert.Single(PlaceImportMappingRules.Map(csv, mapping, null)).Status);
    }

    [Fact]
    public async Task MappingRejectsConflictsMissingColumnsAndCategories()
    {
        var csv = await Csv("Name;Latitude;Longitude\nShop;46;15");
        foreach (var mapping in new[] { new PlaceImportMapping(-1,1,2), new(0,0,2), new(0,1,8), new(0,1,2,Address:1) })
            Assert.Throws<BinImportException>(() => PlaceImportMappingRules.Map(csv,mapping,PlaceCategory.PetShop));
        Assert.Throws<BinImportException>(() => PlaceImportMappingRules.Map(csv,new(0,1,2),null));
        Assert.Throws<BinImportException>(() => PlaceImportMappingRules.Map(csv,new(0,1,2),(PlaceCategory)999));
        Assert.Equal(ImportRowStatus.Invalid, Assert.Single(PlaceImportMappingRules.Map(await Csv("Name;Latitude;Longitude\nShop;46"),new(0,1,2),PlaceCategory.PetShop)).Status);
    }

    [Theory]
    [InlineData("NaN", "15", false)] [InlineData("Infinity", "15", false)] [InlineData("1e2", "15", false)]
    [InlineData("91", "15", false)] [InlineData("46", "181", false)] [InlineData("", "15", false)]
    [InlineData("0", "0", true)] [InlineData("-90", "-180", true)] [InlineData("90", "180", true)]
    [InlineData("46,25", "15,5", true)]
    public async Task CoordinatesFollowFiniteWgs84AndExistingZeroPolicy(string lat, string lon, bool valid)
    {
        var row = Assert.Single(PlaceImportMappingRules.Map(await Csv($"Name;Latitude;Longitude\nShop;{lat};{lon}"), new(0,1,2), PlaceCategory.PetShop));
        Assert.Equal(valid ? ImportRowStatus.Ready : ImportRowStatus.Invalid, row.Status);
    }

    [Theory]
    [InlineData("https://example.org",true)] [InlineData("http://example.org/path",true)]
    [InlineData("javascript:alert(1)",false)] [InlineData("data:text/html,test",false)]
    [InlineData("https://user:password@example.org",false)] [InlineData("https://exa mple.org",false)]
    public void WebsitesReusePlaceRules(string website, bool valid) =>
        Assert.Equal(!valid, PlaceImportMappingRules.Errors(Row() with { Website = website }).Any());

    [Fact]
    public void BoundsMatchNormalPlaceValidationWithoutTruncation()
    {
        var boundary = Row() with { Name = new string('n',120), Address = new string('a',180), Phone = new string('1',40), Description = new string('d',2000) };
        Assert.Empty(PlaceImportMappingRules.Errors(boundary));
        foreach (var invalid in new[] { boundary with { Name = " " }, boundary with { Name = new string('n',121) },
            boundary with { Address = new string('a',181) }, boundary with { Phone = new string('1',41) },
            boundary with { Description = new string('d',2001) }, boundary with { Website = "https://example.org/" + new string('a',500) },
            boundary with { Name = "Bad\nName" }, boundary with { Category = (PlaceCategory)99 } })
            Assert.NotEmpty(PlaceImportMappingRules.Errors(invalid));
    }

    [Fact]
    public async Task ParserIsSharedIncludingCommaQuotesMultilineAndFormulaText()
    {
        var csv = await Csv("\uFEFFName,Latitude,Longitude,Description\n\"=SUM(1,2)\",46,15,\"ČŠŽ \"\"quote\"\"\nline\"",null);
        var row = Assert.Single(PlaceImportMappingRules.Map(csv,new(0,1,2,Description:3),PlaceCategory.PetShop));
        Assert.Equal("=SUM(1,2)",row.Name); Assert.Equal("ČŠŽ \"quote\"\nline",row.Description);
        await Assert.ThrowsAsync<BinImportException>(() => Csv("Name;lat;lon\n\"unfinished"));
        // Full UTF-8, delimiter and byte/row/column/cell/field limit cases run in BinImportTests on this exact parser.
    }

    [Fact] public Task DuplicateRulesAndReadOnlyPreview() => CheckDuplicates();
    [Fact] public Task ImportDefaultsReimportAndPublicEligibility() => CheckImport();
    [Fact] public Task StaleDuplicateSourceAndBatchRevalidation() => CheckRevalidation();
    [Fact] public Task AtomicRollback() => CheckRollback();
    [Fact] public Task BoundedQueriesAndSingleSave() => CheckPerformance();

    [Fact]
    public async Task SessionsEnforceOwnershipExpiryRestartAndMemoryCapacity()
    {
        var clock=new SessionClock();using var sessions=new PlaceImportSessions(clock);
        var csv=await Csv("Name;lat;lon\nShop;46;15");var source=new ImportSource(1,"Test",DataSourceType.Partner,null);
        var first=sessions.Add("admin","test.csv",source,PlaceCategory.PetShop,csv);
        Assert.Equal(32,first.Id.Length);Assert.Null(sessions.Find(first.Id,"other"));
        sessions.Add("admin","second.csv",source,null,csv);
        Assert.Throws<BinImportException>(()=>sessions.Add("admin","third.csv",source,null,csv));
        for(var i=0;i<20;i++)
        {
            try { sessions.Add("other"+i,"test.csv",source,null,csv); }
            catch(BinImportException) { break; }
            Assert.True(i<7,"Store must stop at bounded capacity");
        }
        sessions.Remove(first.Id,"other");Assert.NotNull(sessions.Find(first.Id,"admin"));
        clock.Now=clock.Now.AddMinutes(30);Assert.Null(sessions.Find(first.Id,"admin"));
        using var restarted=new PlaceImportSessions(clock);Assert.Null(restarted.Find(first.Id,"admin"));
        var fresh=sessions.Add("admin","fresh.csv",source,null,csv);sessions.Remove(fresh.Id,"admin");Assert.Null(sessions.Find(fresh.Id,"admin"));
    }
    private sealed class SessionClock:TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }

    [Fact]
    public async Task TooManyExistingCandidatesFailsClosedInsteadOfPartialDetection()
    {
        await using var fixture=await ImportDb.Create(null);
        await fixture.Db.Database.ExecuteSqlRawAsync("""
            WITH RECURSIVE n(x) AS (SELECT 1 UNION ALL SELECT x+1 FROM n WHERE x<50001)
            INSERT INTO Places (Name,Category,Latitude,Longitude,IsActive,IsFeatured,CreatedAt,UpdatedAt)
            SELECT 'Existing',2,46,15,1,0,'2026-01-01','2026-01-01' FROM n;
            """);
        await Assert.ThrowsAsync<BinImportException>(()=>new PlaceImportService(fixture.Db,TimeProvider.System).ClassifyAsync([Row()]));
    }

    // Executed by the isolated PostgreSQL harness, never by production startup or implicit configuration.
    internal static async Task CheckConcurrency(DbContextOptions<ApplicationDbContext> options)
    {
        await using var fixture=await ImportDb.Create(options);
        var pause=new PauseSave();var entered=new LockEntered();
        await using var first=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(pause).Options);
        await using var second=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(options).AddInterceptors(entered).Options);
        var pending=new PlaceImportService(first,TimeProvider.System).ImportAsync(1,[Row()]);
        Task<int>? overlapping=null;
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            overlapping=new PlaceImportService(second,TimeProvider.System).ImportAsync(1,[Row()]);
            await entered.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.False(overlapping.IsCompleted);
        }
        finally { pause.Release.TrySetResult(); }
        Assert.Equal(1,await pending.WaitAsync(TimeSpan.FromSeconds(15)));
        await Assert.ThrowsAsync<BinImportException>(()=>overlapping!.WaitAsync(TimeSpan.FromSeconds(15)));
        Assert.Equal(1,await fixture.Db.Places.CountAsync());
    }
    private sealed class PauseSave:SaveChangesInterceptor
    {
        public TaskCompletionSource Reached {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> r,CancellationToken ct=default)
        { Reached.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(30),ct);return r; }
    }
    private sealed class LockEntered:DbCommandInterceptor
    {
        public TaskCompletionSource Reached {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<int> r,CancellationToken ct=default)
        {if(c.CommandText.Contains("pg_advisory_xact_lock(194721, 191)"))Reached.TrySetResult();return ValueTask.FromResult(r);}
    }

    internal static async Task CheckDuplicates(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await ImportDb.Create(options); var db = fixture.Db;
        db.Places.Add(new Place { Name="  TEST   SHOP ", Category=PlaceCategory.Groomer, Latitude=46, Longitude=15, IsActive=false });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var service = new PlaceImportService(db,TimeProvider.System);
        var result = await service.ClassifyAsync([Row(),Row(3,lat:46+74.9/6371000*180/Math.PI),
            Row(4,"Different business"),Row(5,lat:47),Row(6,lat:47.00001),Row(7,lat:48)]);
        Assert.Equal(new[] { ImportRowStatus.PossibleDuplicate,ImportRowStatus.PossibleDuplicate,ImportRowStatus.Ready,
            ImportRowStatus.Ready,ImportRowStatus.PossibleDuplicate,ImportRowStatus.Ready },result.Select(r=>r.Status));
        Assert.Equal(5,Assert.Single(result[4].Candidates).RowNumber);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(1,await db.Places.CountAsync());
        Assert.Equal(ImportRowStatus.Ready,Assert.Single(await service.ClassifyAsync([Row(lat:46+75.1/6371000*180/Math.PI)])).Status);
        Assert.Equal(ImportRowStatus.PossibleDuplicate,Assert.Single(await service.ClassifyAsync([Row(lat:46+74.9/6371000*180/Math.PI)])).Status);
        Assert.Equal(DuplicateCandidates.NormalizeName("TEST SHOP"),DuplicateCandidates.NormalizeName(" test   shop "));
        var dateLine = await service.ClassifyAsync([Row(lon:179.9999),Row(3,lon:-179.9999)]);
        Assert.Equal(ImportRowStatus.PossibleDuplicate,dateLine[1].Status);
    }
    internal static async Task CheckImport(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await ImportDb.Create(options); var db = fixture.Db;
        var before = JsonSerializer.Serialize(await db.DataSources.AsNoTracking().SingleAsync());
        var service = new PlaceImportService(db,TimeProvider.System);
        Assert.Equal(2,await service.ImportAsync(1,[Row(),Row(3,lat:47)]));
        var places = await db.Places.AsNoTracking().Include(p=>p.Amenities).ToListAsync();
        Assert.All(places,p=> { Assert.True(p.IsActive);Assert.False(p.IsFeatured);Assert.Null(p.FeaturedFrom);Assert.Null(p.FeaturedUntil);
            Assert.Empty(p.Amenities);Assert.Null(p.AmenitiesSourceUrl);Assert.Null(p.AmenitiesVerifiedAt);Assert.Null(p.LogoUrl);Assert.Null(p.ImageUrl);
            Assert.Equal(1,p.DataSourceId);Assert.Equal(p.CreatedAt,p.UpdatedAt);Assert.Equal(0,p.UpdatedAt.Ticks%10); });
        Assert.Equal(before,JsonSerializer.Serialize(await db.DataSources.AsNoTracking().SingleAsync()));
        Assert.Equal(2,await db.Places.ForPublicDetails().CountAsync());
        Assert.All(places,p=>Assert.True(PublicPlaceEligibility.ValidName(p.Name)));
        Assert.All(await service.ClassifyAsync([Row(),Row(3,lat:47)]),r=>Assert.Equal(ImportRowStatus.PossibleDuplicate,r.Status));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row()]));
        Assert.Equal(2,await db.Places.CountAsync()); Assert.Empty(await db.TrashBins.ToListAsync());
        Assert.Empty(await db.UserNotifications.ToListAsync());
    }
    internal static async Task CheckRevalidation(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture = await ImportDb.Create(options); var db = fixture.Db;
        var service = new PlaceImportService(db,TimeProvider.System);
        var preview = await service.ClassifyAsync([Row(),Row(3,lat:47)]);
        db.Places.Add(new Place { Name="Test shop",Latitude=47,Longitude=15,Category=PlaceCategory.PetShop,IsFeatured=true,
            Phone="Original",WebsiteUrl="https://original.example",Description="Original",LogoUrl="original-logo",DataSourceId=1 });
        await db.SaveChangesAsync(); var before=JsonSerializer.Serialize(await db.Places.AsNoTracking().SingleAsync());
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,preview));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(999,[Row()]));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row(),Row(3,lat:46.00001)]));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row(),Row(2,lat:48)]));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row() with { Website="javascript:alert(1)" }]));
        Assert.Equal(before,JsonSerializer.Serialize(await db.Places.AsNoTracking().SingleAsync()));
        await db.DataSources.ExecuteDeleteAsync();
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row()]));
        Assert.Equal(1,await db.Places.CountAsync());
    }
    internal static async Task CheckRollback(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture=await ImportDb.Create(options);
        var before=JsonSerializer.Serialize(await fixture.Db.DataSources.AsNoTracking().SingleAsync());
        var fail=new FailAfterSave();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(fixture.Options).AddInterceptors(fail).Options);
        await Assert.ThrowsAsync<InvalidOperationException>(()=>new PlaceImportService(db,TimeProvider.System).ImportAsync(1,[Row(),Row(3,lat:47)]));
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Empty(await fixture.Db.Places.ToListAsync());
        Assert.Equal(before,JsonSerializer.Serialize(await fixture.Db.DataSources.AsNoTracking().SingleAsync()));
    }
    internal static async Task CheckPerformance(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture=await ImportDb.Create(options); var counter=new Counter();var saves=new Saves();
        await using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(fixture.Options).AddInterceptors(counter,saves).Options);
        var service=new PlaceImportService(db,TimeProvider.System);
        var rows=Enumerable.Range(0,10000).Select(i=>Row(i+2,"Shop "+i,46+i*.00001)).ToArray();
        var preview=await service.ClassifyAsync(rows);
        Assert.Equal(1,counter.Reads); Assert.Equal(0,saves.Count); Assert.Equal(10000,preview.Count);
        Assert.All(preview,r=>Assert.Equal(ImportRowStatus.Ready,r.Status));Assert.InRange(service.LastComparisonCount,0,PlaceImportService.MaxComparisons);
        counter.Reads=0;
        Assert.Equal(1000,await service.ImportAsync(1,rows.Take(1000).ToArray()));
        Assert.Equal(2,counter.Reads);Assert.Equal(1,saves.Count);
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData e,int result,CancellationToken ct=default) => throw new InvalidOperationException("Injected failure after inserts");
    }
    private sealed class Counter : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> r,CancellationToken ct=default)
        { if(c.CommandText.TrimStart().StartsWith("SELECT"))Reads++;return ValueTask.FromResult(r); }
    }
    private sealed class Saves : SaveChangesInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> r,CancellationToken ct=default) {Count++;return ValueTask.FromResult(r);}
    }
    private sealed class ImportDb : IAsyncDisposable
    {
        private string? file;
        public required ApplicationDbContext Db {get;init;}
        public required DbContextOptions<ApplicationDbContext> Options {get;init;}
        public static async Task<ImportDb> Create(DbContextOptions<ApplicationDbContext>? options)
        {
            var path=options==null?Path.Combine(Path.GetTempPath(),$"place-import-{Guid.NewGuid():N}.db"):null;
            options ??= new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options;
            var db=new ApplicationDbContext(options);await db.Database.EnsureCreatedAsync();
            db.DataSources.Add(new DataSource {Name="Synthetic source",Type=DataSourceType.Partner,ContactName="PRIVATE CONTACT",ContactEmail="private@example.invalid",Notes="PRIVATE NOTES"});
            await db.SaveChangesAsync();db.ChangeTracker.Clear();return new() {file=path,Options=options,Db=db};
        }
        public async ValueTask DisposeAsync() {await Db.DisposeAsync();if(file!=null)File.Delete(file);}
    }
}
