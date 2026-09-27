using System.Data.Common;
using System.Globalization;
using System.Text;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.Controllers;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinImportTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"doggydrop-import-{Guid.NewGuid():N}.db");
    private readonly Commands commands = new();
    private readonly SaveCounter saves = new();
    private readonly TransactionCounter transactions = new();
    private DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={file};Pooling=False").AddInterceptors(commands, saves, transactions).Options;
    private async Task<ApplicationDbContext> Db()
    {
        var db = new ApplicationDbContext(Options()); await db.Database.EnsureCreatedAsync();
        db.DataSources.Add(new DataSource { Name = "Municipality", Type = DataSourceType.Municipality, DataDate = new DateOnly(2026, 8, 31) });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return db;
    }
    private static Task<ImportCsv> Csv(string text, string? delimiter = ";") => BinImportCsv.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(text)), delimiter);
    private static ImportRow Row(int number, double lat = 46, double lon = 15) => new(number, "Koš", lat, lon, null, ImportRowStatus.Ready, []);

    [Theory]
    [InlineData(";", "\uFEFF lat ; lon ;ime\r\n46,39123;15,57420;\"Črni; koš \"\"Ž\"\"\nŠ\"", "Črni; koš \"Ž\"\nŠ")]
    [InlineData(",", "lat,lon,ime\n\"46,39123\",\"15,57420\",\"Črni, koš\"", "Črni, koš")]
    [InlineData(";", "lat;lon;ime\n46.39123;15.57420;=SUM(1+1)", "=SUM(1+1)")]
    public async Task ParsesQuotedCsvBomSlovenianMultilineAndPlainFormulas(string delimiter, string text, string name)
    {
        var csv = await Csv(text, null); Assert.Equal(delimiter[0], csv.Delimiter);
        var row = Assert.Single(BinImportMapping.Map(csv, new(0, 1, 2, null)));
        Assert.Equal(46.39123, row.Latitude); Assert.Equal(15.57420, row.Longitude); Assert.Equal(name, row.Name);
    }

    [Theory]
    [InlineData("")] [InlineData("lat;lon")] [InlineData("lat;lat\n46;15")]
    [InlineData(";lon\n46;15")] [InlineData("lat;lon\n\"46;15")]
    [InlineData("lat;lon\n46\"bad;15")] [InlineData("lat;lon\n\"46\"tail;15")]
    [InlineData("lat;lon\n46;15\0")]
    [InlineData("46;15\n46.1;15.1")]
    public async Task RejectsInvalidCsv(string text) => await Assert.ThrowsAsync<BinImportException>(() => Csv(text));

    [Fact]
    public async Task DelimiterAmbiguityRequiresExplicitChoiceAndLegacyBytesAreRejected()
    {
        await Assert.ThrowsAsync<BinImportException>(() => Csv("lat,lon;name\n46,15;Č", null));
        Assert.Equal(2, (await Csv("lat,lon;name\n46,15;Č", ";")).Headers.Length);
        await Assert.ThrowsAsync<BinImportException>(() => BinImportCsv.ReadAsync(new MemoryStream([0x9a, 0x3b, 0x31]), ";"));
    }

    [Fact]
    public async Task ParserEnforcesByteRowFieldColumnAndCellBounds()
    {
        await Assert.ThrowsAsync<BinImportException>(() => Csv(new string('x', BinImportCsv.MaxBytes + 1)));
        await Assert.ThrowsAsync<BinImportException>(() => Csv("lat;lon\n" + new string('x', BinImportCsv.MaxField + 1) + ";15"));
        await Assert.ThrowsAsync<BinImportException>(() => Csv("lat;lon\n" + string.Concat(Enumerable.Repeat("46;15\n", BinImportCsv.MaxRows + 1))));
        Assert.Equal(10000, (await Csv("lat;lon\n" + string.Concat(Enumerable.Repeat("46;15\n", BinImportCsv.MaxRows)))).Rows.Count);
        await Assert.ThrowsAsync<BinImportException>(() => Csv(string.Join(';', Enumerable.Range(0,65)) + "\n1;2"));
        var header = string.Join(';', Enumerable.Range(0,64).Select(i => "c" + i));
        await Assert.ThrowsAsync<BinImportException>(() => Csv(header + "\n" + string.Concat(Enumerable.Repeat(string.Join(';',Enumerable.Repeat("1",64)) + "\n", 3200))));
    }

    [Theory]
    [InlineData("NaN")] [InlineData("Infinity")] [InlineData("")] [InlineData("46,1.2")]
    [InlineData("46,39123,15,57420")] [InlineData("1e2")] [InlineData("46 123")]
    public void CoordinatesRejectAmbiguousAndNonFiniteNumbers(string value) => Assert.Null(BinImportMapping.Coordinate(value));

    [Theory]
    [InlineData("-90", "-180", true)] [InlineData("90", "180", true)] [InlineData("0", "0", true)]
    [InlineData("-90.0001", "15", false)] [InlineData("46", "-180.0001", false)]
    [InlineData("Infinity", "15", false)] [InlineData("46", "", false)]
    public async Task CoordinateRangesMatchExistingFiniteWgs84Policy(string lat, string lon, bool valid)
    {
        var row=Assert.Single(BinImportMapping.Map(await Csv($"lat;lon\n{lat};{lon}"),new(0,1,null,null)));
        Assert.Equal(valid ? ImportRowStatus.Ready : ImportRowStatus.Invalid,row.Status);
        if(valid)Assert.True(NearbyDiscoveryService.ValidCoordinate(row.Latitude!.Value,row.Longitude!.Value));
    }

    [Fact]
    public async Task MappingIsExplicitSupportsOverrideAndShowsExactDisplayName()
    {
        var csv = await Csv("ime;gps_lon;gps_lat;naslov\nKoš;15;46;Glavna 1");
        Assert.Equal(new ImportMapping(2,1,0,3), ImportMapping.Suggest(csv.Headers));
        var row = Assert.Single(BinImportMapping.Map(csv, new(2,1,0,3)));
        Assert.Equal("Koš · Glavna 1", row.Name); Assert.Equal(46, row.Latitude);
        var swapped = Assert.Single(BinImportMapping.Map(csv, new(1,2,0,null)));
        Assert.Equal(15, swapped.Latitude); Assert.Equal(46, swapped.Longitude);
        Assert.Throws<BinImportException>(() => BinImportMapping.Map(csv,new(-1,1,null,null)));
        Assert.Throws<BinImportException>(() => BinImportMapping.Map(csv,new(1,1,null,null)));
        Assert.Throws<BinImportException>(() => BinImportMapping.Map(csv,new(2,1,1,null)));
        Assert.Equal(-1,ImportMapping.Suggest(["x","y"]).Latitude);
        Assert.Equal(ImportRowStatus.Invalid, Assert.Single(BinImportMapping.Map(await Csv("x;y\n400000;150000"),new(0,1,null,null))).Status);
        Assert.Equal("Koš",Assert.Single(BinImportMapping.Map(await Csv("a;b\n0;0"),new(0,1,null,null))).Name);
    }

    [Fact]
    public async Task BadRowsAreInvalidWithoutDiscardingGoodRows()
    {
        var csv = await Csv("lat;lon;name\n46;15;Good\n91;15;Outside\n46;181;Outside\nNaN;15;Bad\n46;15\n46;15;" + new string('x',201));
        var rows = BinImportMapping.Map(csv,new(0,1,2,null));
        Assert.Equal(ImportRowStatus.Ready,rows[0].Status);
        Assert.All(rows.Skip(1),r=>Assert.Equal(ImportRowStatus.Invalid,r.Status));
        Assert.Equal(Enumerable.Range(2,6),rows.Select(r=>r.Number));
        Assert.Contains("WGS84",rows[1].Error);
    }

    [Fact]
    public async Task ClassifiesExistingAndWithinFileDuplicatesWithSourceStatusAndNoWrites()
    {
        await using var db = await Db();
        db.TrashBins.Add(new TrashBin { Name="Existing",Latitude=46,Longitude=15,DataSourceId=1,IsApproved=false });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); commands.Sql.Clear();
        var result = await new BinImportService(db,TimeProvider.System).ClassifyAsync([Row(2),Row(3,47),Row(4,47.00001),Row(5,48)]);
        Assert.Equal([ImportRowStatus.PossibleDuplicate,ImportRowStatus.Ready,ImportRowStatus.PossibleDuplicate,ImportRowStatus.Ready],result.Select(r=>r.Status));
        Assert.Equal("Municipality",Assert.Single(result[0].Candidates).Source); Assert.False(result[0].Candidates[0].Approved);
        Assert.Equal(3,Assert.Single(result[2].Candidates).RowNumber); Assert.InRange(result[2].Candidates[0].Metres,1,2);
        Assert.Single(commands.Sql); Assert.Contains("LIMIT",commands.Sql[0]); Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(1,await db.TrashBins.CountAsync());
    }

    [Theory]
    [InlineData(0, true)] [InlineData(19.9, true)] [InlineData(20.1, false)]
    public async Task ExistingCandidatesUseTheEpic18Threshold(double metres, bool duplicate)
    {
        await using var db=await Db();
        db.TrashBins.Add(new TrashBin {Name="Existing",Latitude=46,Longitude=15,DataSourceId=1,IsApproved=true});await db.SaveChangesAsync();
        var latitude=46+metres/6371000*180/Math.PI;
        var row=Assert.Single(await new BinImportService(db,TimeProvider.System).ClassifyAsync([Row(2,latitude)]));
        Assert.Equal(duplicate ? ImportRowStatus.PossibleDuplicate : ImportRowStatus.Ready,row.Status);
        if(duplicate)Assert.Equal("Municipality",Assert.Single(row.Candidates).Source);
    }

    [Fact]
    public async Task WithinFileChainsConservativelyFlagLaterRowsAgainstExcludedEarlierRows()
    {
        await using var db=await Db();
        var step=15d/6371000*180/Math.PI;
        var rows=await new BinImportService(db,TimeProvider.System).ClassifyAsync([Row(2),Row(3,46+step),Row(4,46+2*step),Row(5,47)]);
        Assert.Equal([ImportRowStatus.Ready,ImportRowStatus.PossibleDuplicate,ImportRowStatus.PossibleDuplicate,ImportRowStatus.Ready],rows.Select(r=>r.Status));
        Assert.Equal(2,Assert.Single(rows[1].Candidates).RowNumber);
        Assert.Equal(3,Assert.Single(rows[2].Candidates).RowNumber); // A-C >20m, but B-C <20m even though B is excluded.
        Assert.Empty(await db.TrashBins.ToListAsync());
    }

    [Fact]
    public async Task ImportIsApprovedAttributedOnlyToSourceAndDoesNotAwardOrNotify()
    {
        await using var db = await Db(); var sourceBefore = await db.DataSources.AsNoTracking().SingleAsync();
        db.Users.Add(new ApplicationUser { Id="nearby-user",UserName="Nearby" });
        db.NearbyDiscoveryPreferences.Add(new NearbyDiscoveryPreference { UserId="nearby-user",Latitude=46,Longitude=15,
            RadiusMeters=10000,BinsEnabled=true,EnabledAt=DateTime.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var service = new BinImportService(db,TimeProvider.System);
        Assert.Equal(2,await service.ImportAsync(1,[Row(2),Row(3,47)]));
        var bins = await db.TrashBins.AsNoTracking().ToListAsync();
        Assert.All(bins,b=>{Assert.True(b.IsApproved);Assert.Equal(1,b.DataSourceId);Assert.Null(b.UserId);Assert.Equal(b.DateAdded,b.ApprovedAt);});
        Assert.Empty(await db.UserNotifications.ToListAsync()); Assert.Empty(await db.UserAchievements.ToListAsync());
        Assert.Empty(await db.UserXpEvents.ToListAsync()); Assert.Empty(await db.UserGamificationProfiles.ToListAsync());
        Assert.Equal(0,await db.TrashBins.CountAsync(b=>b.UserId=="nearby-user"));
        var sourceAfter=await db.DataSources.AsNoTracking().SingleAsync();
        Assert.Equal(sourceBefore.DataDate,sourceAfter.DataDate);Assert.Equal(sourceBefore.UpdatedAt,sourceAfter.UpdatedAt);
        Assert.All(await service.ClassifyAsync([Row(2),Row(3,47)]),r=>Assert.Equal(ImportRowStatus.PossibleDuplicate,r.Status));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row(2),Row(3,47)]));
        Assert.Equal(2,await db.TrashBins.CountAsync());
    }

    [Fact]
    public async Task RevalidatesNewDuplicatesMissingSourceAndManipulatedBatchBeforeWriting()
    {
        await using var db = await Db();var service = new BinImportService(db,TimeProvider.System);
        var preview = await service.ClassifyAsync([Row(2),Row(3,47)]);
        db.TrashBins.Add(new TrashBin { Name="New after preview",Latitude=47,Longitude=15 });await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,preview));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(999,[Row(2)]));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row(2),Row(3,46.00001)]));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,[Row(2),Row(2,48)]));
        Assert.Equal(1,await db.TrashBins.CountAsync());
    }

    [Fact]
    public async Task DatabaseFailureRollsBackWholeImport()
    {
        await using var db = await Db();
        db.TrashBins.Add(new TrashBin { Name="Unrelated",Latitude=48,Longitude=16,DataSourceId=1,IsApproved=false,UsedCount=7 });
        await db.SaveChangesAsync();
        var beforeSource=await db.DataSources.AsNoTracking().SingleAsync();
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_import BEFORE INSERT ON TrashBins WHEN NEW.Latitude = 47 BEGIN SELECT RAISE(ABORT, 'test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(()=>new BinImportService(db,TimeProvider.System).ImportAsync(1,[Row(2),Row(3,47)]));
        await using var read = new ApplicationDbContext(Options());
        var existing=Assert.Single(await read.TrashBins.ToListAsync());Assert.Equal("Unrelated",existing.Name);Assert.False(existing.IsApproved);
        Assert.Equal(48,existing.Latitude);Assert.Equal(16,existing.Longitude);Assert.Equal(7,existing.UsedCount);Assert.Equal(1,existing.DataSourceId);
        var afterSource=await read.DataSources.SingleAsync();Assert.Equal(beforeSource.Name,afterSource.Name);
        Assert.Equal(beforeSource.DataDate,afterSource.DataDate);Assert.Equal(beforeSource.UpdatedAt,afterSource.UpdatedAt);
    }

    [Fact]
    public async Task BatchUsesOneSaveOneCommitAndConstantReadQueryCount()
    {
        await using var db=await Db();saves.Count=0;transactions.Commits=0;commands.Sql.Clear();
        var rows=Enumerable.Range(0,1000).Select(i=>Row(i+2,46+i*.001)).ToArray();
        Assert.Equal(1000,await new BinImportService(db,TimeProvider.System).ImportAsync(1,rows));
        Assert.Equal(1,saves.Count);Assert.Equal(1,transactions.Commits);
        Assert.Equal(2,commands.Sql.Count(sql=>sql.TrimStart().StartsWith("SELECT",StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(1000,await db.TrashBins.CountAsync());
    }

    [Fact]
    public async Task ImportedBinsWorkWithEpic18FiltersCountsDuplicateReviewAndSetNull()
    {
        await using var db=await Db();await new BinImportService(db,TimeProvider.System).ImportAsync(1,[Row(2)]);
        var list=Assert.IsAssignableFrom<IReadOnlyList<AdminBinRow>>(Assert.IsType<ViewResult>(await new AdminBinsController(db).Index("approved",1)).Model);
        Assert.Equal("Municipality",Assert.Single(list).SourceName);
        var sources=Assert.IsAssignableFrom<IReadOnlyList<DataSourceRow>>(Assert.IsType<ViewResult>(await new AdminDataSourcesController(db).Index()).Model);
        Assert.Equal(1,Assert.Single(sources).Bins);
        db.TrashBins.Add(new TrashBin { Name="Nearby existing",Latitude=46.00001,Longitude=15 });await db.SaveChangesAsync();
        var pair=Assert.Single((await new DuplicateCandidates(db).FindAsync(BulkTarget.Bins,46,15,1000)).Pairs);
        Assert.Contains(new[]{pair.First.Source,pair.Second.Source},source=>source=="Municipality");
        db.ChangeTracker.Clear();
        var controller=new AdminDataSourcesController(db) {ControllerContext=new ControllerContext {HttpContext=new DefaultHttpContext()}};
        controller.TempData=new TempDataDictionary(controller.HttpContext,new EmptyTempData());
        await controller.DeleteConfirmed(1);
        var imported=await db.TrashBins.AsNoTracking().SingleAsync(b=>b.Name=="Koš");Assert.True(imported.IsApproved);Assert.Null(imported.DataSourceId);Assert.Null(imported.UserId);
        Assert.Equal(2,await db.TrashBins.CountAsync());
    }

    [Fact]
    public async Task TenThousandRowsUseOneDuplicateQueryAndBoundedSpatialChecks()
    {
        await using var db = await Db(); commands.Sql.Clear();
        var rows=Enumerable.Range(0,10000).Select(i=>Row(i+2,-70+i*.01)).ToArray();
        var service=new BinImportService(db,TimeProvider.System);
        var result=await service.ClassifyAsync(rows);
        Assert.Equal(10000,result.Count);Assert.All(result,r=>Assert.Equal(ImportRowStatus.Ready,r.Status));Assert.Single(commands.Sql);
        Assert.True(service.LastComparisonCount < rows.Length * 10);
        var dense=await service.ClassifyAsync(rows.Select(r=>r with {Latitude=46}).ToArray());
        Assert.Equal(9999,dense.Count(r=>r.Status==ImportRowStatus.PossibleDuplicate));Assert.All(dense,r=>Assert.InRange(r.Candidates.Count,0,3));
        Assert.True(service.LastComparisonCount <= rows.Length * 3);
    }

    [Fact]
    public async Task CoordinateEdgesAndRecordLimitFailSafely()
    {
        await using var db=await Db();var service=new BinImportService(db,TimeProvider.System);
        var edge=await service.ClassifyAsync([Row(2,0,179.99999),Row(3,0,-179.99999),Row(4,89.99999,0),Row(5,89.99999,180)]);
        Assert.Equal(2,edge.Count(r=>r.Status==ImportRowStatus.PossibleDuplicate));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ClassifyAsync([Row(2,double.NaN)]));
        // Insert in SQL to keep this boundary test inexpensive; exactly one bounded reader is used afterwards.
        await db.Database.ExecuteSqlRawAsync("""
            WITH RECURSIVE numbers(n) AS (SELECT 1 UNION ALL SELECT n+1 FROM numbers WHERE n < 50001)
            INSERT INTO TrashBins (Name,Latitude,Longitude,DateAdded,IsApproved,UsedCount,FullReports,MissingReports,UsefulVotes,NotUsefulVotes)
            SELECT 'Existing',46,15,'2026-09-01',0,0,0,0,0,0 FROM numbers;
            """);
        commands.Sql.Clear();
        await Assert.ThrowsAsync<BinImportException>(()=>service.ClassifyAsync([Row(2)]));
        Assert.Single(commands.Sql);Assert.Contains("LIMIT",commands.Sql[0]);Assert.Equal(0,service.LastComparisonCount);
    }

    [Fact]
    public async Task SessionsAreBoundToOwnerExpireAndLimitCapacity()
    {
        var clock=new TestClock();using var sessions=new BinImportSessions(clock);
        var csv=await Csv("lat;lon\n46;15");var source=new ImportSource(1,"Source",DataSourceType.Manual,null);
        var first=sessions.Add("alice","file.csv",source,csv);
        Assert.Null(sessions.Find(first.Id,"bob"));Assert.Same(first,sessions.Find(first.Id,"alice"));
        sessions.Add("alice","file.csv",source,csv);
        Assert.Throws<BinImportException>(()=>sessions.Add("alice","file.csv",source,csv));
        clock.Now=clock.Now.AddMinutes(31);Assert.Null(sessions.Find(first.Id,"alice"));
        Assert.NotNull(sessions.Add("alice","file.csv",source,csv));
    }
    private sealed class TestClock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Commands : DbCommandInterceptor
    {
        public List<string> Sql { get; }=[];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken cancellationToken=default)
        {Sql.Add(command.CommandText);return ValueTask.FromResult(result);}
    }
    private sealed class SaveCounter : SaveChangesInterceptor
    {
        public int Count;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data,InterceptionResult<int> result,CancellationToken cancellationToken=default)
        {Count++;return ValueTask.FromResult(result);}
    }
    private sealed class TransactionCounter : DbTransactionInterceptor
    {
        public int Commits;
        public override Task TransactionCommittedAsync(DbTransaction transaction,TransactionEndEventData data,CancellationToken cancellationToken=default)
        {Commits++;return Task.CompletedTask;}
    }
    private sealed class EmptyTempData : ITempDataProvider
    {
        public IDictionary<string,object> LoadTempData(HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(HttpContext context,IDictionary<string,object> values) { }
    }
    public void Dispose() { if(File.Exists(file))File.Delete(file); }
}
