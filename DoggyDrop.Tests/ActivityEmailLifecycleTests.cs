using DoggyDrop.Data;
using DoggyDrop.Controllers;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class ActivityEmailLifecycleTests
{
    [Theory][InlineData(true,false)][InlineData(false,false)][InlineData(true,true)][InlineData(false,true)]
    public async Task ActualNewBinDecisionIsAtomicWithEmail(bool approve,bool fail) {
        await using var f=await PrivacyTestDatabase.Create(interceptor:fail?new FailOutbox():null);await ActivityEmailTests.Seed(f.Db);
        await f.Db.TrashBins.ExecuteUpdateAsync(s=>s.SetProperty(b=>b.IsApproved,false));f.Db.ChangeTracker.Clear();
        var bin=await f.Db.TrashBins.SingleAsync();var snapshot=BinCommunityRules.Snapshot(bin);
        var notifications=new Notifications();var calendar=new TestGamificationCalendar();
        var controller=new MapController(f.Db,null!,f.Users,null!,null!,notifications,new GamificationService(f.Db,notifications,calendar),null!,null!,null!,calendar,null!){
            ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"b"),new Claim(ClaimTypes.Role,"Admin")],"synthetic"))}},Url=new Links()};
        Task<IActionResult> Decide()=>approve?controller.Approve(1,snapshot):controller.Reject(1,null,snapshot);
        if(fail)await Assert.ThrowsAsync<DbUpdateException>(Decide);
        else {Assert.IsType<RedirectToActionResult>(await Decide());await Decide();}
        f.Db.ChangeTracker.Clear();var saved=await f.Db.TrashBins.SingleAsync();
        Assert.Equal(!fail&&approve,saved.IsApproved);Assert.Equal(!fail&&!approve,saved.IsRejected);
        if(fail)Assert.Empty(await f.Db.NotificationOutbox.ToListAsync());
        else Assert.Equal(approve?ActivityEmailType.BinApproved:ActivityEmailType.BinRejected,(await f.Db.NotificationOutbox.SingleAsync()).Type);
    }
    private sealed class Notifications:INotificationService {
        public Task CreateAsync(string userId,string type,string title,string body,string? linkUrl=null)=>Task.CompletedTask;
        public Task CreateUniqueRecentAsync(string userId,string type,string title,string body,string? linkUrl=null,int withinHours=24)=>Task.CompletedTask;
    }
    private sealed class Links:IUrlHelper {
        public ActionContext ActionContext {get;}=new();public string? Action(UrlActionContext c)=>"/Map/MyBins";
        public string? Content(string? p)=>p;public bool IsLocalUrl(string? u)=>true;public string? Link(string? r,object? v)=>"/Map/MyBins";public string? RouteUrl(UrlRouteContext c)=>"/Map/MyBins";
    }
    [Theory][InlineData(1,true)][InlineData(1,false)][InlineData(2,true)][InlineData(2,false)][InlineData(3,true)][InlineData(3,false)]
    public async Task ActualReviewQueuesOnlyTruthfulResultWithNoReceipt(int kind,bool accept) => await CheckReview(null,kind,accept);
    internal static async Task CheckReview(DbContextOptions<ApplicationDbContext>? options,int kind,bool accept) {
        await using var f=await PrivacyTestDatabase.Create(options);await ActivityEmailTests.Seed(f.Db);
        var photos=new Photos();var service=new BinContributions(f.Db,photos,photos,new BinPhotoReferences(f.Options),NullLogger<BinContributions>.Instance);
        var id=await service.SubmitAsync(1,"a",Guid.NewGuid(),kind==1?BinContributionType.Photo:BinContributionType.Issue,
            kind==2?BinIssueReason.WRONG_LOCATION:kind==3?BinIssueReason.DAMAGED:null,"private-description",kind==2?46.001:null,kind==2?15.001:null,null,
            kind==1?new FormFile(new MemoryStream([1,2,3]),0,3,"Photo","synthetic.jpg"):null);
        Assert.Empty(await f.Db.NotificationOutbox.ToListAsync());
        await service.ReviewAsync(id,"b",accept,false,"PRIVATE-ADMIN-NOTE");
        var row=await f.Db.NotificationOutbox.AsNoTracking().SingleAsync();
        var contribution=await f.Db.BinContributions.AsNoTracking().SingleAsync();
        Assert.Equal(ActivityEmails.ReviewType(contribution,accept),row.Type);Assert.Equal("a",row.RecipientUserId);Assert.Equal(id,row.ContributionId);
        await Assert.ThrowsAsync<BinReviewException>(()=>service.ReviewAsync(id,"b",accept,false,null));Assert.Single(await f.Db.NotificationOutbox.ToListAsync());
        var mail=new ActivityEmailTests.Mail { Outcome=EmailFailure.Transient };
        await ActivityEmailTests.Delivery(f.Db,mail,new()).RunBatchAsync();
        Assert.Equal(accept?BinContributionStatus.Approved:BinContributionStatus.Rejected,(await f.Db.BinContributions.AsNoTracking().SingleAsync()).Status);
        Assert.DoesNotContain("PRIVATE-ADMIN-NOTE",mail.Sent.Single().Message.Html);Assert.DoesNotContain("private-description",mail.Sent.Single().Message.Text);
        Assert.DoesNotContain("46.001",mail.Sent.Single().Message.Text);
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task ReviewRollbackRollsBackIntent(bool accept) {
        var fail=new FailOutbox();await using var f=await PrivacyTestDatabase.Create(interceptor:fail);await ActivityEmailTests.Seed(f.Db);
        var bin=await f.Db.TrashBins.SingleAsync();var item=new BinContribution {BinId=1,SubmittedByUserId="a",Type=BinContributionType.Issue,Reason=BinIssueReason.DAMAGED,RequestId=Guid.NewGuid(),BinSnapshot=BinCommunityRules.Snapshot(bin)};
        f.Db.BinContributions.Add(item);await f.Db.SaveChangesAsync();var photos=new Photos();
        var service=new BinContributions(f.Db,photos,photos,new BinPhotoReferences(f.Options),NullLogger<BinContributions>.Instance);
        await Assert.ThrowsAsync<DbUpdateException>(()=>service.ReviewAsync(item.Id,"b",accept,false,null));f.Db.ChangeTracker.Clear();
        Assert.Empty(await f.Db.NotificationOutbox.ToListAsync());Assert.Equal(BinContributionStatus.Pending,(await f.Db.BinContributions.SingleAsync()).Status);Assert.Empty(await f.Db.UserNotifications.ToListAsync());
    }
    [Fact] public async Task OptOutDoesNotBlockBusinessReview() {
        await using var f=await PrivacyTestDatabase.Create();await ActivityEmailTests.Seed(f.Db);f.Db.NotificationPreferences.Add(new(){UserId="a",ContributionUpdates=false});await f.Db.SaveChangesAsync();
        var bin=await f.Db.TrashBins.SingleAsync();var item=new BinContribution{BinId=1,SubmittedByUserId="a",Type=BinContributionType.Issue,Reason=BinIssueReason.DAMAGED,RequestId=Guid.NewGuid(),BinSnapshot=BinCommunityRules.Snapshot(bin)};f.Db.BinContributions.Add(item);await f.Db.SaveChangesAsync();var photos=new Photos();
        await new BinContributions(f.Db,photos,photos,new BinPhotoReferences(f.Options),NullLogger<BinContributions>.Instance).ReviewAsync(item.Id,"b",true,false,null);
        Assert.Empty(await f.Db.NotificationOutbox.ToListAsync());Assert.Equal(BinContributionStatus.Approved,(await f.Db.BinContributions.SingleAsync()).Status);
    }
    private sealed class FailOutbox:SaveChangesInterceptor {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> result,CancellationToken ct=default) {
            if(e.Context!.ChangeTracker.Entries<NotificationOutbox>().Any(x=>x.State==EntityState.Added))throw new DbUpdateException("synthetic rollback");return ValueTask.FromResult(result);
        }
    }
    private sealed class Photos:ICloudinaryService,IBinPhotoStorage {
        public Task<string?> UploadTrashBinImageAsync(IFormFile file)=>Task.FromResult<string?>("/uploads/trashbins/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp");
        public Task<string?> UploadImageAsync(IFormFile file)=>throw new NotSupportedException();public Task<string?> UploadWalkImageAsync(IFormFile file)=>throw new NotSupportedException();
        public bool CanRotate(string? url)=>false;public Task<string?> RotateCopyAsync(string url,int degrees)=>throw new NotSupportedException();public Task DeleteManagedAsync(string url)=>Task.CompletedTask;
    }
}
