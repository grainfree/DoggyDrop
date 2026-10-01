using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinContributionTests : IAsyncLifetime
{
    private readonly string file = Path.Combine(Path.GetTempPath(), "bin-community-" + Guid.NewGuid() + ".db");
    private DbContextOptions<ApplicationDbContext> Options => new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={file};Pooling=False").Options;
    private ApplicationDbContext Db() => new(Options);
    private readonly Photos photos = new();
    private BinContributions Service(ApplicationDbContext db) => new(db, photos, photos, new BinPhotoReferences(Options), NullLogger<BinContributions>.Instance);
    private static IFormFile Photo() => new FormFile(new MemoryStream([1,2,3]), 0, 3, "Photo", "test.jpg");
    public async Task InitializeAsync()
    {
        await using var db = Db(); await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new ApplicationUser { Id = "a", UserName = "a" }, new ApplicationUser { Id = "b", UserName = "b" }, new ApplicationUser { Id = "admin", UserName = "admin" });
        db.DataSources.Add(new DataSource { Id = 1, Name = "OpenStreetMap — koši za pasje iztrebke", Type = DataSourceType.PublicDataset, ContactEmail = "private@example.test", Notes = "PRIVATE" });
        db.TrashBins.AddRange(new TrashBin { Id = 1, Name = "OSM bin", Latitude = 46, Longitude = 15, IsApproved = true, DataSourceId = 1, UsedCount = 10, MissingReports = 1 },
            new TrashBin { Id = 2, Name = "User bin", Latitude = 46.01, Longitude = 15, IsApproved = true, UserId = "a" },
            new TrashBin { Id = 3, Name = "Admin bin", Latitude = 46.02, Longitude = 15, IsApproved = true, UserId = "admin" },
            new TrashBin { Id = 4, Name = "Pending", Latitude = 46.03, Longitude = 15 });
        await db.SaveChangesAsync();
    }
    public Task DisposeAsync() { File.Delete(file); return Task.CompletedTask; }
    private Task<long> Issue(ApplicationDbContext db, BinIssueReason reason = BinIssueReason.BIN_MISSING, string user = "a", double? lat = null, double? lon = null, int? duplicate = null, int bin = 1) =>
        Service(db).SubmitAsync(bin, user, Guid.NewGuid(), BinContributionType.Issue, reason, "<script>alert(1)</script>", lat, lon, duplicate, null);

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task AllOriginsAcceptEvidenceWithoutPublicMutation(int id)
    {
        await using var db = Db(); var bin = await db.TrashBins.FindAsync(id); var before = BinCommunityRules.Snapshot(bin!);
        await Issue(db, bin:id); var photo = await Service(db).SubmitAsync(id, "a", Guid.NewGuid(), BinContributionType.Photo, null, null, null, null, null, Photo());
        Assert.Equal(before, BinCommunityRules.Snapshot((await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == id))));
        Assert.Equal(BinContributionStatus.Pending, (await db.BinContributions.FindAsync(photo))!.Status);
        await Service(db).ReviewAsync(photo, "admin", true, false, null);
        var current = await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == id);
        Assert.Equal(photos.Url, current.ImageUrl); Assert.Equal(bin!.DataSourceId, current.DataSourceId); Assert.Equal(bin.UserId, current.UserId);
        Assert.True(current.IsApproved); Assert.False(current.IsRetired); Assert.Empty(await db.UserXpEvents.ToListAsync());
    }
    [Fact]
    public async Task MissingRetiresWithoutDeletingAndReactivationPreservesIdentityHistory()
    {
        await using var db = Db(); var id = await Issue(db); Assert.Equal(1, (await db.TrashBins.FindAsync(1))!.MissingReports);
        await Service(db).ReviewAsync(id, "admin", true, false, "Confirmed");
        var bin = await db.TrashBins.AsNoTracking().SingleAsync(b => b.Id == 1);
        Assert.True(bin.IsRetired); Assert.True(bin.IsApproved); Assert.Equal(1, bin.DataSourceId); Assert.Equal(10, bin.UsedCount);
        Assert.False(await db.TrashBins.PublicBins().AnyAsync(b => b.Id == 1));
        await Service(db).LifecycleAsync(1, BinCommunityRules.Snapshot(bin), false);
        Assert.True(await db.TrashBins.PublicBins().AnyAsync(b => b.Id == 1)); Assert.Single(await db.BinContributions.ToListAsync());
        Assert.Single(await db.UserNotifications.ToListAsync());
    }
    [Theory]
    [InlineData(BinIssueReason.DAMAGED)] [InlineData(BinIssueReason.OTHER)] [InlineData(BinIssueReason.NOT_PUBLIC)]
    public async Task ResolutionDoesNotRequireRetirementOrAlterLegacyScore(BinIssueReason reason)
    {
        await using var db = Db(); var before = BinCommunityRules.Snapshot((await db.TrashBins.FindAsync(1))!);
        await Service(db).ReviewAsync(await Issue(db, reason), "admin", true, false, null);
        var bin = await db.TrashBins.FindAsync(1); Assert.Equal(before, BinCommunityRules.Snapshot(bin!)); Assert.Equal(1, bin!.MissingReports);
    }
    [Theory]
    [InlineData(null, null)] [InlineData(46.1, null)] [InlineData(null, 15.1)] [InlineData(double.NaN, 15d)]
    [InlineData(double.PositiveInfinity, 15d)] [InlineData(90d, 15d)] [InlineData(46d, 181d)] [InlineData(46d,15d)] [InlineData(0d,0d)]
    public async Task InvalidLocationNeverPersists(double? lat, double? lon)
    {
        await using var db = Db(); await Assert.ThrowsAsync<BinReviewException>(() => Issue(db, BinIssueReason.WRONG_LOCATION, lat:lat, lon:lon));
        Assert.Empty(await db.BinContributions.ToListAsync()); Assert.Empty(photos.Deleted);
    }
    [Fact]
    public async Task LocationApprovalRechecksNewPendingDuplicate()
    {
        await using var db = Db(); var id = await Issue(db, BinIssueReason.WRONG_LOCATION, lat:46.001, lon:15);
        db.TrashBins.Add(new TrashBin { Name = "Racing pending", Latitude = 46.001, Longitude = 15 }); await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BinReviewException>(() => Service(db).ReviewAsync(id, "admin", true, false, null));
        await using var read = Db(); Assert.Equal(46, (await read.TrashBins.FindAsync(1))!.Latitude);
        Assert.Equal(BinContributionStatus.Pending, (await read.BinContributions.FindAsync(id))!.Status);
    }
    [Fact]
    public async Task LocationApprovedAtomicallyAndSourcePreserved()
    {
        await using var db = Db(); var id = await Issue(db, BinIssueReason.WRONG_LOCATION, lat:46.001, lon:15);
        Assert.Equal(46, (await db.TrashBins.FindAsync(1))!.Latitude);
        await Service(db).ReviewAsync(id, "admin", true, false, null);
        Assert.Equal(46.001, (await db.TrashBins.FindAsync(1))!.Latitude); Assert.Equal(1, (await db.TrashBins.FindAsync(1))!.DataSourceId);
        await Assert.ThrowsAsync<BinReviewException>(() => Service(db).ReviewAsync(id, "admin", true, false, null));
        Assert.Single(await db.UserNotifications.ToListAsync());
    }
    [Theory]
    [InlineData(BinContributionType.Issue)] [InlineData(BinContributionType.Photo)]
    public async Task NewerBinEditMakesApprovalConflictButAllowsRejection(BinContributionType type)
    {
        long id; await using (var db = Db()) id = await Service(db).SubmitAsync(1,"a",Guid.NewGuid(),type,BinIssueReason.BIN_MISSING,null,null,null,null,type==BinContributionType.Photo?Photo():null);
        await using (var winner = Db()) { var b = (await winner.TrashBins.FindAsync(1))!; b.Name = "Newer name"; await winner.SaveChangesAsync(); }
        await using var review = Db(); await Assert.ThrowsAsync<BinReviewException>(() => Service(review).ReviewAsync(id,"admin",true,false,null));
        await Service(review).ReviewAsync(id,"admin",false,false,null);
        Assert.Equal("Newer name",(await review.TrashBins.FindAsync(1))!.Name);
    }
    [Fact]
    public async Task DuplicatePendingPerUserReasonIsBoundedButOtherUsersAllowed()
    {
        await using var db = Db(); var a = await Issue(db); Assert.Equal(a, await Issue(db)); Assert.NotEqual(a, await Issue(db,user:"b"));
        Assert.Equal(2,await db.BinContributions.CountAsync());
    }
    [Theory]
    [InlineData(null)] [InlineData(1)] [InlineData(999)]
    public async Task DuplicateMustIdentifyDifferentExistingPublicBin(int? other)
    { await using var db=Db(); await Assert.ThrowsAsync<BinReviewException>(()=>Issue(db,BinIssueReason.DUPLICATE,duplicate:other)); }
    [Fact]
    public async Task DuplicateCanRetireOnlyReportedRecordWithoutMerging()
    {
        await using var db=Db(); var id=await Issue(db,BinIssueReason.DUPLICATE,duplicate:2);
        await Service(db).ReviewAsync(id,"admin",true,true,null);
        Assert.True((await db.TrashBins.FindAsync(1))!.IsRetired); Assert.False((await db.TrashBins.FindAsync(2))!.IsRetired);
        Assert.Equal(4,await db.TrashBins.CountAsync());
    }
    [Fact]
    public async Task DuplicateApprovalRevalidatesOtherBinsCurrentPublicEligibility()
    {
        long id;await using(var db=Db())id=await Issue(db,BinIssueReason.DUPLICATE,duplicate:2);
        await using(var changed=Db()){(await changed.TrashBins.FindAsync(2))!.IsRetired=true;await changed.SaveChangesAsync();}
        await using var review=Db();await Assert.ThrowsAsync<BinReviewException>(()=>Service(review).ReviewAsync(id,"admin",true,true,null));
        Assert.False((await review.TrashBins.FindAsync(1))!.IsRetired);Assert.Equal(BinContributionStatus.Pending,(await review.BinContributions.FindAsync(id))!.Status);
    }
    [Fact]
    public async Task ReplayedPhotoDoesNotUploadAgainOrAwardAnything()
    {
        await using var db=Db(); var request=Guid.NewGuid();
        var first=await Service(db).SubmitAsync(1,"a",request,BinContributionType.Photo,null,null,null,null,null,Photo());
        var second=await Service(db).SubmitAsync(1,"a",request,BinContributionType.Photo,null,null,null,null,null,Photo());
        Assert.Equal(first,second);Assert.Equal(1,photos.Uploads);Assert.Empty(await db.UserXpEvents.ToListAsync());
    }
    [Fact]
    public async Task RejectedPhotoCleanupDoesNotChangePublicPhotoAndFailureDoesNotUndoReview()
    {
        await using var db=Db(); var bin=(await db.TrashBins.FindAsync(1))!;bin.ImageUrl="/uploads/old.webp";await db.SaveChangesAsync();
        var id=await Service(db).SubmitAsync(1,"a",Guid.NewGuid(),BinContributionType.Photo,null,null,null,null,null,Photo());
        photos.ThrowCleanup=true;await Service(db).ReviewAsync(id,"admin",false,false,null);
        Assert.Equal("/uploads/old.webp",bin.ImageUrl);Assert.Equal(BinContributionStatus.Rejected,(await db.BinContributions.FindAsync(id))!.Status);
        Assert.Single(photos.Deleted);
    }
    [Fact]
    public async Task PhotoReplacementKeepsSharedOldAssetAndPendingReferences()
    {
        await using var db=Db();var bins=await db.TrashBins.Where(b=>b.Id<=2).ToListAsync();foreach(var b in bins)b.ImageUrl="/uploads/shared.webp";await db.SaveChangesAsync();
        var id=await Service(db).SubmitAsync(1,"a",Guid.NewGuid(),BinContributionType.Photo,null,null,null,null,null,Photo());
        Assert.True(await new BinPhotoReferences(Options).IsReferencedAsync(photos.Url));
        await Service(db).ReviewAsync(id,"admin",true,false,null);Assert.Empty(photos.Deleted);
    }
    [Theory]
    [InlineData(0)] [InlineData(12582913)]
    public async Task EmptyOrOversizePhotoNeverUploads(long length)
    {
        await using var db=Db(); var file=new FormFile(Stream.Null,0,length,"Photo","test.jpg");
        await Assert.ThrowsAsync<BinReviewException>(()=>Service(db).SubmitAsync(1,"a",Guid.NewGuid(),BinContributionType.Photo,null,null,null,null,null,file));Assert.Equal(0,photos.Uploads);
    }
    [Fact]
    public async Task UploadFailureNeverCreatesPendingEvidence()
    { await using var db=Db();photos.FailUpload=true;await Assert.ThrowsAsync<BinReviewException>(()=>Service(db).SubmitAsync(1,"a",Guid.NewGuid(),BinContributionType.Photo,null,null,null,null,null,Photo()));Assert.Empty(await db.BinContributions.ToListAsync()); }
    [Fact]
    public async Task StaleRetirementDoesNotOverwriteNewerBin()
    {
        await using var db=Db();var bin=(await db.TrashBins.FindAsync(1))!;var snapshot=BinCommunityRules.Snapshot(bin);bin.Name="Changed";await db.SaveChangesAsync();
        await Assert.ThrowsAsync<BinReviewException>(()=>Service(db).LifecycleAsync(1,snapshot,true));Assert.False(bin.IsRetired);
    }
    [Fact]
    public async Task InfrastructureCompareAndSwapDetectsConcurrentAdminEdit()
    {
        await using var a=Db();await using var b=Db();var first=(await a.TrashBins.FindAsync(1))!;var second=(await b.TrashBins.FindAsync(1))!;
        first.IsRetired=true;await a.SaveChangesAsync();second.Latitude=46.002;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>b.SaveChangesAsync());
    }
    [Fact]
    public void SubmissionLimitsAreDeterministicBoundedAndResetWithoutSleep()
    {
        var time=new Clock();var limits=new BinSubmissionLimits(time);for(var i=0;i<10;i++)Assert.True(limits.Take("a"));Assert.False(limits.Take("a"));Assert.True(limits.Take("b"));
        time.Now=time.Now.AddHours(1);Assert.True(limits.Take("a"));
    }
    [Fact]
    public async Task DatabaseFailureAfterUploadRetainsAssetForUnknownOutcomeReconciliation()
    {
        var options=new DbContextOptionsBuilder<ApplicationDbContext>(Options).AddInterceptors(new FailEvidenceSave()).Options;
        await using var db=new ApplicationDbContext(options);
        await Assert.ThrowsAsync<BinReviewException>(()=>Service(db).SubmitAsync(1,"a",Guid.NewGuid(),BinContributionType.Photo,null,null,null,null,null,Photo()));
        await using var read=Db();Assert.False(await read.BinContributions.AnyAsync());Assert.Null((await read.TrashBins.FindAsync(1))!.ImageUrl);
        Assert.Equal(1,photos.Uploads);Assert.Empty(photos.Deleted);
    }
    [Fact]
    public void MigrationAddsOnlyLifecycleAndEvidenceWithoutRewritingExistingData()
    {
        var migration=new DoggyDrop.Migrations.AddBinCommunityContributions();
        var columns=migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.AddColumnOperation>().ToList();
        Assert.Equal(new[]{"IsRejected","IsRetired","RejectedAt"},columns.Select(c=>c.Name));
        Assert.All(columns.Where(c=>c.ClrType==typeof(bool)),c=>Assert.Equal(false,c.DefaultValue));
        Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.CreateTableOperation>());
        Assert.DoesNotContain(migration.UpOperations,o=>o is Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation or Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation or Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation);
    }
    [Fact]
    public async Task AccountDeletionRemovesPrivateEvidenceButPreservesApprovedInfrastructure()
    {
        await using var fixture=await PrivacyTestDatabase.Create();var db=fixture.Db;
        db.Users.AddRange(new ApplicationUser{Id="owner",UserName="owner"},new ApplicationUser{Id="other",UserName="other"});
        var source=new DataSource{Name="OSM"};var bin=new TrashBin{Name="Public",Latitude=46,Longitude=15,IsApproved=true,DataSource=source,ImageUrl="approved-photo"};db.TrashBins.Add(bin);await db.SaveChangesAsync();
        var pending=new BinContribution{BinId=bin.Id,SubmittedByUserId="owner",Type=BinContributionType.Photo,Description="private draft",ProposedPhotoUrl="pending-photo",RequestId=Guid.NewGuid()};
        var accepted=new BinContribution{BinId=bin.Id,SubmittedByUserId="owner",Type=BinContributionType.Photo,Status=BinContributionStatus.Approved,Description="private accepted text",ProposedPhotoUrl="approved-photo",RequestId=Guid.NewGuid()};
        var other=new BinContribution{BinId=bin.Id,SubmittedByUserId="other",Type=BinContributionType.Issue,Reason=BinIssueReason.OTHER,Description="other text",RequestId=Guid.NewGuid()};
        db.BinContributions.AddRange(pending,accepted,other);await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var cleanup=new RecordingCleanup();Assert.True((await new AccountDataDeletion(db,fixture.Users,cleanup).DeleteAsync(await db.Users.SingleAsync(u=>u.Id=="owner"))).Succeeded);db.ChangeTracker.Clear();
        var current=await db.TrashBins.SingleAsync();Assert.Equal(source.Id,current.DataSourceId);Assert.Equal("approved-photo",current.ImageUrl);Assert.True(current.IsApproved);
        var rejected=await db.BinContributions.SingleAsync(c=>c.Id==pending.Id);Assert.Equal(BinContributionStatus.Rejected,rejected.Status);Assert.Null(rejected.ProposedPhotoUrl);Assert.Null(rejected.SubmittedByUserId);Assert.Null(rejected.Description);
        var preserved=await db.BinContributions.SingleAsync(c=>c.Id==accepted.Id);Assert.Equal(BinContributionStatus.Approved,preserved.Status);Assert.Null(preserved.SubmittedByUserId);Assert.Null(preserved.Description);
        Assert.Equal("other text",(await db.BinContributions.SingleAsync(c=>c.Id==other.Id)).Description);Assert.Equal(new[]{"pending-photo"},cleanup.Urls);
    }
    [Fact]
    public async Task PersonalExportIncludesOnlyOwnedContributionFieldsWithoutReviewOrPhotoUrls()
    {
        await using var db=Db();var own=await Issue(db);await Issue(db,user:"b");
        var item=(await db.BinContributions.FindAsync(own))!;item.ReviewNote="ADMIN-SECRET";item.ReviewedByUserId="admin";item.ProposedPhotoUrl="PRIVATE-EVIDENCE-URL";await db.SaveChangesAsync();
        await using var stream=new MemoryStream();await new PersonalDataExport(db).WriteAsync("a",stream);
        using var json=System.Text.Json.JsonDocument.Parse(stream.ToArray());var row=Assert.Single(json.RootElement.GetProperty("BinMaintenanceContributions").EnumerateArray());Assert.Equal(own,row.GetProperty("Id").GetInt64());
        foreach(var field in new[]{"ProposedPhotoUrl","ReviewNote","ReviewedByUserId","SubmittedByUserId"})Assert.False(row.TryGetProperty(field,out _));
        var text=System.Text.Encoding.UTF8.GetString(stream.ToArray());Assert.DoesNotContain("ADMIN-SECRET",text);Assert.DoesNotContain("PRIVATE-EVIDENCE-URL",text);
    }
    private sealed class FailEvidenceSave:Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData e,Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result,CancellationToken ct=default)
        {if(e.Context!.ChangeTracker.Entries<BinContribution>().Any(x=>x.State==EntityState.Added))throw new DbUpdateException("synthetic");return ValueTask.FromResult(result);}
    }
    private sealed class Clock:TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class Photos:ICloudinaryService,IBinPhotoStorage
    {
        public string Url="/uploads/trashbins/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp";public int Uploads;public bool FailUpload,ThrowCleanup;public List<string> Deleted=[];
        public Task<string?> UploadTrashBinImageAsync(IFormFile f){Uploads++;return Task.FromResult<string?>(FailUpload?null:Url);}
        public Task<string?> UploadImageAsync(IFormFile f)=>throw new NotSupportedException();public Task<string?> UploadWalkImageAsync(IFormFile f)=>throw new NotSupportedException();
        public bool CanRotate(string? s)=>false;public Task<string?> RotateCopyAsync(string s,int d)=>throw new NotSupportedException();
        public Task DeleteManagedAsync(string url){Deleted.Add(url);if(ThrowCleanup)throw new IOException();return Task.CompletedTask;}
    }
}
