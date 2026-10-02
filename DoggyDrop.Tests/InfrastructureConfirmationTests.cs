using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class InfrastructureConfirmationTests
{
    [Theory]
    [InlineData(0, 5, null)] [InlineData(40, 10, null)] [InlineData(50, 0, null)]
    [InlineData(50.001, 0, "far")] [InlineData(60, 5, "far")]
    [InlineData(0, 300, "accuracy")] [InlineData(49, 5, "accuracy")]
    [InlineData(25, 25, null)] [InlineData(25, 25.01, "accuracy")]
    public void AccuracyEnvelope(double distance, double accuracy, string? expected) =>
        Assert.Equal(expected, InfrastructureConfirmations.Proximity(distance, accuracy));

    [Theory]
    [InlineData(null, 15d, 5d)] [InlineData(46d, null, 5d)] [InlineData(46d, 15d, null)]
    [InlineData(double.NaN, 15d, 5d)] [InlineData(46d, double.PositiveInfinity, 5d)]
    [InlineData(91d, 15d, 5d)] [InlineData(46d, 181d, 5d)] [InlineData(46d, 15d, -1d)]
    [InlineData(46d, 15d, double.NaN)] [InlineData(46d, 15d, 10001d)]
    public void InvalidCoordinates(double? lat, double? lon, double? accuracy) => Assert.False(InfrastructureConfirmations.Valid(new(lat, lon, accuracy)));
    [Fact] public void ZeroAndWgs84EdgesAreExplicitlyValid() {
        Assert.True(InfrastructureConfirmations.Valid(new(0, 0, 0))); Assert.True(InfrastructureConfirmations.Valid(new(-90, -180, 1)));
        Assert.True(InfrastructureConfirmations.Valid(new(90, 180, 1))); Assert.False(InfrastructureConfirmations.Valid(null));
    }
    [Theory] [InlineData(0,"recent")] [InlineData(30,"recent")] [InlineData(31,"older")] [InlineData(180,"older")] [InlineData(181,"stale")]
    public void RecencyWindows(int days,string expected) {
        var now=new DateTime(2026,10,2,12,0,0,DateTimeKind.Utc);
        Assert.Equal(expected,InfrastructureTrust.Summarize(now.AddDays(-days),1,false,now).State);
        Assert.Equal("issue",InfrastructureTrust.Summarize(now.AddDays(-days),1,true,now).State);
    }

    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task AcceptsEvidenceWithoutChangingInfrastructure(bool water) {
        await using var f=await Fixture.Create();
        Assert.Equal("accepted",(await f.Service.ConfirmAsync(water,1,"a",new(46,15,5))).Outcome);
        var c=await f.Db.InfrastructureConfirmations.SingleAsync(); Assert.Equal(f.Clock.Now.UtcDateTime,c.CreatedAt);Assert.Equal("a",c.UserId);
        Assert.Equal(water?InfrastructureConfirmationType.WaterPointWorking:InfrastructureConfirmationType.TrashBinPresent,c.Type);
        var bin=await f.Db.TrashBins.SingleAsync();Assert.Equal("b",bin.UserId);Assert.Equal(1,bin.DataSourceId);Assert.Equal(7,bin.UsedCount);
        var p=await f.Db.WaterPoints.SingleAsync();Assert.Equal(1,p.DataSourceId);Assert.Equal(WaterSeasonality.Seasonal,p.Seasonality);
        Assert.Equal(WaterPotability.SourceReportedDrinking,p.Potability);Assert.Equal(WaterAccess.Unknown,p.Access);
        Assert.Empty(await f.Db.Walks.ToListAsync());Assert.Empty(await f.Db.UserXpEvents.ToListAsync());Assert.Empty(await f.Db.UserNotifications.ToListAsync());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CooldownBoundaryAndIndependentUsers(bool water) {
        await using var f=await Fixture.Create(); var location=new ConfirmationLocation(46,15,5);
        Assert.Equal("accepted",(await f.Service.ConfirmAsync(water,1,"a",location)).Outcome);
        Assert.Equal("cooldown",(await f.Service.ConfirmAsync(water,1,"a",location)).Outcome);
        Assert.Equal(2,(await f.Service.ConfirmAsync(water,1,"b",location)).Summary!.RecentUniqueConfirmers);
        f.Clock.Now=f.Clock.Now.AddHours(24).AddTicks(-1);Assert.Equal("cooldown",(await f.Service.ConfirmAsync(water,1,"a",location)).Outcome);
        f.Clock.Now=f.Clock.Now.AddTicks(1);Assert.Equal("accepted",(await f.Service.ConfirmAsync(water,1,"a",location)).Outcome);
        Assert.Equal(3,await f.Db.InfrastructureConfirmations.CountAsync());
        var summary=water?await f.Trust.WaterAsync():await f.Trust.BinsAsync();Assert.Equal(2,summary[1].RecentUniqueConfirmers);
        f.Clock.Now=f.Clock.Now.AddDays(181);summary=water?await f.Trust.WaterAsync():await f.Trust.BinsAsync();
        Assert.Equal("stale",summary[1].State);Assert.Equal(0,summary[1].RecentUniqueConfirmers);Assert.Equal(3,await f.Db.InfrastructureConfirmations.CountAsync());
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task PhysicalAndLifecycleChangesInvalidateButPreserveHistory(bool water) {
        await using var f=await Fixture.Create();await f.Service.ConfirmAsync(water,1,"a",new(46,15,5));
        if(water)(await f.Db.WaterPoints.SingleAsync()).Name="Renamed";else (await f.Db.TrashBins.SingleAsync()).Name="Renamed";
        await f.Db.SaveChangesAsync();Assert.Single(water?await f.Trust.WaterAsync():await f.Trust.BinsAsync());
        if(water)(await f.Db.WaterPoints.SingleAsync()).IsRetired=true;else (await f.Db.TrashBins.SingleAsync()).IsRetired=true;
        await f.Db.SaveChangesAsync();Assert.Empty(water?await f.Trust.WaterAsync():await f.Trust.BinsAsync());
        Assert.Equal("unavailable",(await f.Service.ConfirmAsync(water,1,"b",new(46,15,5))).Outcome);
        if(water)(await f.Db.WaterPoints.SingleAsync()).IsRetired=false;else (await f.Db.TrashBins.SingleAsync()).IsRetired=false;
        await f.Db.SaveChangesAsync();Assert.Empty(water?await f.Trust.WaterAsync():await f.Trust.BinsAsync());Assert.Single(await f.Db.InfrastructureConfirmations.ToListAsync());
        Assert.Equal("accepted",(await f.Service.ConfirmAsync(water,1,"b",new(46,15,5))).Outcome);
        if(water)(await f.Db.WaterPoints.SingleAsync()).Latitude=47;else (await f.Db.TrashBins.SingleAsync()).Latitude=47;
        await f.Db.SaveChangesAsync();Assert.Empty(water?await f.Trust.WaterAsync():await f.Trust.BinsAsync());
        Assert.Equal("far",(await f.Service.ConfirmAsync(water,1,"a",new(46,15,5))).Outcome);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task PendingMissingAndPoorFixRejectWithoutWrite(bool water) {
        await using var f=await Fixture.Create();
        foreach(var (input,expected) in new[]{(new ConfirmationLocation(46,15,300),"accuracy"),(new ConfirmationLocation(0,0,5),"far"),(new ConfirmationLocation(null,15,5),"invalid")})
            Assert.Equal(expected,(await f.Service.ConfirmAsync(water,1,"a",input)).Outcome);
        if(water)(await f.Db.WaterPoints.SingleAsync()).IsApproved=false;else (await f.Db.TrashBins.SingleAsync()).IsApproved=false;
        await f.Db.SaveChangesAsync();Assert.Equal("unavailable",(await f.Service.ConfirmAsync(water,1,"a",new(46,15,5))).Outcome);
        Assert.Equal("unavailable",(await f.Service.ConfirmAsync(water,999,"a",new(46,15,5))).Outcome);
        Assert.Empty(await f.Db.InfrastructureConfirmations.ToListAsync());
    }
    [Fact]public async Task IssuePriorityAndRejectionResolutionNeverExposePrivateReport() {
        await using var f=await Fixture.Create();await f.Service.ConfirmAsync(false,1,"a",new(46,15,5));
        var issue=new BinContribution{BinId=1,Type=BinContributionType.Issue,Reason=BinIssueReason.DAMAGED,Description="PRIVATE",SubmittedByUserId="b",RequestId=Guid.NewGuid()};
        f.Db.BinContributions.Add(issue);await f.Db.SaveChangesAsync();var summary=(await f.Trust.BinsAsync())[1];Assert.Equal("issue",summary.State);Assert.NotNull(summary.LastConfirmedAt);
        Assert.DoesNotContain("PRIVATE",System.Text.Json.JsonSerializer.Serialize(summary));
        issue.Status=BinContributionStatus.Approved;await f.Db.SaveChangesAsync();Assert.Equal("issue",(await f.Trust.BinsAsync())[1].State);
        issue.Status=BinContributionStatus.Rejected;await f.Db.SaveChangesAsync();Assert.Equal("recent",(await f.Trust.BinsAsync())[1].State);
    }
    [Fact]public async Task UserDeletionAnonymizesEvidenceAndDoesNotCountUnknownAuthors() {
        await using var f=await Fixture.Create();await f.Service.ConfirmAsync(false,1,"a",new(46,15,5));
        await f.Db.Users.Where(u=>u.Id=="a").ExecuteDeleteAsync();f.Db.ChangeTracker.Clear();
        Assert.Null((await f.Db.InfrastructureConfirmations.SingleAsync()).UserId);var summary=(await f.Trust.BinsAsync())[1];
        Assert.NotNull(summary.LastConfirmedAt);Assert.Equal(0,summary.RecentUniqueConfirmers);
        await Assert.ThrowsAsync<SqliteException>(()=>f.Db.TrashBins.ExecuteDeleteAsync());
    }
    [Fact]public async Task TypedFkAndCheckConstraintRejectOrphansAndWrongType() {
        await using var f=await Fixture.Create();
        foreach(var c in new[]{new InfrastructureConfirmation{TrashBinId=1,WaterPointId=1,Type=InfrastructureConfirmationType.TrashBinPresent},new InfrastructureConfirmation{TrashBinId=99,Type=InfrastructureConfirmationType.TrashBinPresent},new InfrastructureConfirmation{TrashBinId=1,Type=InfrastructureConfirmationType.WaterPointWorking}}){
            c.CreatedAt=f.Clock.Now.UtcDateTime;f.Db.InfrastructureConfirmations.Add(c);await Assert.ThrowsAsync<DbUpdateException>(()=>f.Db.SaveChangesAsync());f.Db.ChangeTracker.Clear();}
    }
    public sealed class Clock:TimeProvider{public DateTimeOffset Now=new(2026,10,2,12,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;}
    public sealed class Fixture:IAsyncDisposable {
        private readonly SqliteConnection connection;public ApplicationDbContext Db{get;}public Clock Clock{get;}=new();
        public InfrastructureConfirmations Service=>new(Db,Clock);public InfrastructureTrust Trust=>new(Db,Clock);
        private Fixture(SqliteConnection c){connection=c;Db=new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(c).Options);}
        public static async Task<Fixture>Create(){var c=new SqliteConnection("Data Source=:memory:");await c.OpenAsync();var f=new Fixture(c);await f.Db.Database.EnsureCreatedAsync();
            f.Db.Users.AddRange(new ApplicationUser{Id="a"},new ApplicationUser{Id="b"});f.Db.DataSources.Add(new(){Id=1,Name="OpenStreetMap",Notes="PRIVATE"});
            f.Db.TrashBins.Add(new(){Id=1,Name="Koš",Latitude=46,Longitude=15,IsApproved=true,DataSourceId=1,UserId="b",UsedCount=7});
            f.Db.WaterPoints.Add(new(){Id=1,Latitude=46,Longitude=15,IsApproved=true,DataSourceId=1,Potability=WaterPotability.SourceReportedDrinking,Seasonality=WaterSeasonality.Seasonal});
            await f.Db.SaveChangesAsync();return f;}
        public async ValueTask DisposeAsync(){await Db.DisposeAsync();await connection.DisposeAsync();}
    }
}
