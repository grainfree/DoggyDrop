using System.ComponentModel.DataAnnotations;
using System.Text;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace DoggyDrop.Tests;

public sealed class WaterPointTests
{
    public static WaterImportRow Row(int n=2,double lat=46,double lon=15)=>new(n,"",lat,lon,0,0,0,WaterPotability.SourceReportedDrinking,null,ImportRowStatus.Ready,[]);
    [Theory][InlineData("", "15",false)][InlineData("NaN","15",false)][InlineData("Infinity","15",false)]
    [InlineData("91","15",false)][InlineData("46","181",false)][InlineData("0","0",true)][InlineData("-90","-180",true)]
    [InlineData("46,5","15,2",true)]
    public async Task CoordinatesAndUnnamedSemantics(string lat,string lon,bool valid){
        var csv=await BinImportCsv.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes($"Latitude;Longitude\n{lat};{lon}")),";");
        var row=Assert.Single(WaterImportMappingRules.Map(csv,WaterImportMapping.Suggest(csv.Headers)));
        Assert.Equal(valid?ImportRowStatus.Ready:ImportRowStatus.Invalid,row.Status);Assert.Equal("",row.Name);
    }
    [Theory][InlineData("Unknown","SourceReportedDrinking",true)][InlineData("Public","Unknown",false)]
    [InlineData("Restricted","SourceReportedDrinking",false)][InlineData("Public","NotDrinking",false)]
    [InlineData("private","yes",false)][InlineData("99","1",false)][InlineData("Permissive","SourceReportedDrinking",true)]
    public async Task ContradictoryAndInvalidMappedEvidenceCannotBeOverridden(string access,string potability,bool valid){
        var csv=await BinImportCsv.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes($"Latitude;Longitude;Access;Potability\n46;15;{access};{potability}")),";");
        Assert.Equal(valid?ImportRowStatus.Ready:ImportRowStatus.Invalid,Assert.Single(WaterImportMappingRules.Map(csv,WaterImportMapping.Suggest(csv.Headers))).Status);
    }
    [Fact]public async Task MappingBoundsAndUnsafeControlCharacters(){
        var csv=await BinImportCsv.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("Latitude;Longitude\n46;15")),";");
        foreach(var m in new[]{new WaterImportMapping(-1,1),new(0,0),new(0,9),new(0,1,0)})Assert.Throws<BinImportException>(()=>WaterImportMappingRules.Map(csv,m));
        Assert.NotEmpty(WaterImportMappingRules.Errors(Row() with{Name="bad\nname"}));
        Assert.NotEmpty(WaterImportMappingRules.Errors(Row() with{Name=new string('x',201)}));
    }
    [Theory][InlineData(true,false,1,0,1)][InlineData(false,false,1,0,0)][InlineData(true,true,1,0,0)]
    [InlineData(false,false,0,0,0)][InlineData(false,false,2,3,0)]
    public async Task LifecycleAndExplicitPublicAllowlist(bool approved,bool retired,int potable,int access,int count){
        await using var f=await Fixture.Create();var p=new WaterPoint{Latitude=46,Longitude=15,Potability=(WaterPotability)potable,Access=(WaterAccess)access,IsApproved=approved,IsRetired=retired,DataSourceId=1};
        f.Db.WaterPoints.Add(p);await f.Db.SaveChangesAsync();var items=await WaterPoints.LoadAsync(f.Db.WaterPoints);Assert.Equal(count,items.Count);
        if(count==1){Assert.Equal("Pitnik",items[0].Name);var json=System.Text.Json.JsonSerializer.Serialize(items);Assert.DoesNotContain("PRIVATE",json);Assert.DoesNotContain("UpdatedAt",json);Assert.Null(items[0].SourceUrl);}
    }
    [Theory][InlineData(false,false)][InlineData(true,false)][InlineData(true,true)]
    public async Task ActivePendingAndRetiredBlockGeographicRecreation(bool approved,bool retired){
        await using var f=await Fixture.Create();f.Db.WaterPoints.Add(new(){Latitude=46,Longitude=15,IsApproved=approved,IsRetired=retired,Potability=WaterPotability.SourceReportedDrinking,Name="Different name"});await f.Db.SaveChangesAsync();
        var service=new WaterImportService(f.Db,TimeProvider.System);var delta=180/Math.PI/6371000;
        var rows=await service.ClassifyAsync([Row(),Row(3,46+14.999*delta),Row(4,47),Row(5,47.00001)]);
        Assert.Equal(new[]{ImportRowStatus.PossibleDuplicate,ImportRowStatus.PossibleDuplicate,ImportRowStatus.Ready,ImportRowStatus.PossibleDuplicate},rows.Select(r=>r.Status));
        Assert.Equal(ImportRowStatus.Ready,Assert.Single(await service.ClassifyAsync([Row(lat:46+15.001*delta)])).Status);
        Assert.Equal(1,await f.Db.WaterPoints.CountAsync());
    }
    [Fact]public async Task FinalRevalidationReplayAndSourceAreAtomic(){
        await using var f=await Fixture.Create();var service=new WaterImportService(f.Db,TimeProvider.System);
        var preview=await service.ClassifyAsync([Row(),Row(3,47)]);Assert.Equal(0,await f.Db.WaterPoints.CountAsync());
        Assert.Equal(1,await service.ImportAsync(1,[Row()]));
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(1,preview));Assert.Equal(1,await f.Db.WaterPoints.CountAsync());
        await Assert.ThrowsAsync<BinImportException>(()=>service.ImportAsync(99,[Row(lat:48)]));Assert.Equal(1,await f.Db.WaterPoints.CountAsync());
        var p=await f.Db.WaterPoints.SingleAsync();Assert.True(p.IsApproved);Assert.False(p.IsRetired);Assert.Null(p.Name);Assert.Equal(1,p.DataSourceId);Assert.NotNull(p.ApprovedAt);
    }
    [Fact]public async Task TransactionRollsBackPartialInsert(){
        await using var f=await Fixture.Create();await f.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_water BEFORE INSERT ON WaterPoints WHEN NEW.Latitude=47 BEGIN SELECT RAISE(ABORT, 'synthetic'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(()=>new WaterImportService(f.Db,TimeProvider.System).ImportAsync(1,[Row(),Row(3,47)]));
        Assert.Equal(0,await f.Db.WaterPoints.CountAsync());Assert.Empty(f.Db.ChangeTracker.Entries());
    }
    [Fact]public async Task SourceDeletionSetsNullAndStaleTokenIsRejected(){
        await using var f=await Fixture.Create();await new WaterImportService(f.Db,TimeProvider.System).ImportAsync(1,[Row()]);
        var p=await f.Db.WaterPoints.SingleAsync();var original=p.UpdatedAt;
        await f.Db.WaterPoints.ExecuteUpdateAsync(s=>s.SetProperty(p=>p.UpdatedAt,PlaceUpdates.NextUpdatedAt(original)));
        p.Name="stale";await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();
        await f.Db.DataSources.ExecuteDeleteAsync();Assert.Null((await f.Db.WaterPoints.SingleAsync()).DataSourceId);
    }
    [Fact]public async Task GeneratedOwnerBatchUsesActualParserMappingAndDuplicateRules(){
        var path=Environment.GetEnvironmentVariable("DOGGYDROP_WATER_BATCH");if(string.IsNullOrEmpty(path))return;
        await using var file=File.OpenRead(path);var csv=await BinImportCsv.ReadAsync(file,",");
        var rows=WaterImportMappingRules.Map(csv,WaterImportMapping.Suggest(csv.Headers));Assert.NotEmpty(rows);Assert.All(rows,r=>Assert.Equal(ImportRowStatus.Ready,r.Status));
        await using var f=await Fixture.Create();Assert.All(await new WaterImportService(f.Db,TimeProvider.System).ClassifyAsync(rows),r=>Assert.Equal(ImportRowStatus.Ready,r.Status));Assert.Equal(0,await f.Db.WaterPoints.CountAsync());
        Console.WriteLine($"Water batch: {rows.Count} parsed, {rows.Count} valid, 0 invalid, 0 duplicates, 0 writes.");
    }
    [Fact]public async Task PreviewSessionsAreOwnerBoundExpireAndReleaseCapacity(){
        var clock=new PreviewClock();using var sessions=new WaterImportSessions(clock);
        var csv=await BinImportCsv.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes("Latitude;Longitude\n46;15")),";");
        var source=new ImportSource(1,"Source",DataSourceType.PublicDataset,null);var first=sessions.Add("admin","a.csv",source,csv);
        Assert.Null(sessions.Find(first.Id,"other"));sessions.Remove(first.Id,"other");Assert.NotNull(sessions.Find(first.Id,"admin"));
        sessions.Add("admin","b.csv",source,csv);Assert.Throws<BinImportException>(()=>sessions.Add("admin","c.csv",source,csv));
        clock.Now=clock.Now.AddMinutes(30);Assert.Null(sessions.Find(first.Id,"admin"));Assert.NotNull(sessions.Add("admin","fresh.csv",source,csv));
    }
    private sealed class PreviewClock:TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}

    private sealed class Fixture:IAsyncDisposable{
        private readonly SqliteConnection connection;public ApplicationDbContext Db{get;}
        private Fixture(SqliteConnection c){connection=c;Db=new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(c).Options);}
        public static async Task<Fixture>Create(){var c=new SqliteConnection("Data Source=:memory:");await c.OpenAsync();var f=new Fixture(c);await f.Db.Database.EnsureCreatedAsync();f.Db.DataSources.Add(new(){Name="OSM",ContactName="PRIVATE",Notes="PRIVATE",WebsiteUrl="javascript:alert(1)"});await f.Db.SaveChangesAsync();return f;}
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await connection.DisposeAsync();}
    }
}
