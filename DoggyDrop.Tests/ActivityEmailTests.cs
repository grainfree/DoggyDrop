using System.Text;
using System.Text.Json;
using System.Data.Common;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class ActivityEmailTests
{
    [Theory][InlineData(null)][InlineData("false")][InlineData("malformed")]
    public async Task WorkerWithoutExplicitValidOptInCompletesWithoutResolvingServices(string? enabled) {
        await using var services=new ServiceCollection().BuildServiceProvider();
        using var worker=new NotificationWorker(services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["Notifications:DeliveryEnabled"]=enabled}).Build(),
            new TestEnvironment{EnvironmentName="Production"},NullLogger<NotificationWorker>.Instance);
        await worker.StartAsync(CancellationToken.None);await worker.ExecuteTask!;
        Assert.True(worker.ExecuteTask.IsCompletedSuccessfully);
    }
    public static ActivityEmailTemplate Templates() => new(new SeoSite(new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string,string?> { ["Seo:PublicOrigin"]="https://doggydrop.example" }).Build(), new TestEnvironment()));
    public sealed class TestEnvironment : IHostEnvironment {
        public string EnvironmentName { get; set; }="Testing"; public string ApplicationName { get; set; }="Tests";
        public string ContentRootPath { get; set; }=""; public IFileProvider ContentRootFileProvider { get; set; }=new NullFileProvider();
    }
    public sealed class Clock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow; public override DateTimeOffset GetUtcNow()=>Now; }
    public sealed class Mail : IEmailTransport {
        public List<(string Recipient, RenderedEmail Message)> Sent=[]; public EmailFailure Outcome; public bool Throw;
        public Task<EmailFailure> SendAsync(string recipient, RenderedEmail email, CancellationToken ct) {
            ct.ThrowIfCancellationRequested(); Sent.Add((recipient,email));
            if(Throw) throw new IOException("synthetic-secret recipient@example.test provider body"); return Task.FromResult(Outcome);
        }
    }
    public static NotificationDelivery Delivery(ApplicationDbContext db, Mail mail, Clock clock) => new(db,mail,Templates(),clock,NullLogger<NotificationDelivery>.Instance);
    internal static async Task Seed(ApplicationDbContext db) {
        db.Users.AddRange(new ApplicationUser { Id="a",UserName="a",Email="a@example.test",EmailConfirmed=true },
            new ApplicationUser { Id="b",UserName="b",Email="b@example.test",EmailConfirmed=true });
        db.TrashBins.Add(new TrashBin { Id=1,Name="<script>hostile</script>",Latitude=46,Longitude=15,IsApproved=true,UserId="a" });
        await db.SaveChangesAsync();
    }
    internal static async Task Queue(ApplicationDbContext db, ActivityEmailType type=ActivityEmailType.BinApproved) {
        await ActivityEmails.QueueAsync(db,type,"a",1); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
    }
    [Theory]
    [InlineData(ActivityEmailType.BinApproved)] [InlineData(ActivityEmailType.BinRejected)]
    [InlineData(ActivityEmailType.PhotoApproved)] [InlineData(ActivityEmailType.PhotoRejected)]
    [InlineData(ActivityEmailType.LocationApproved)] [InlineData(ActivityEmailType.LocationRejected)]
    [InlineData(ActivityEmailType.IssueApproved)] [InlineData(ActivityEmailType.IssueRejected)]
    public void TemplatesHaveSafeHtmlTextAndCanonicalLinks(ActivityEmailType type) {
        var mail=Templates().Render(new NotificationOutbox {Type=type,BinId=12},"</p><script>alert('x')</script><img src=x onerror=bad>\r\nBcc:evil",true);
        Assert.Contains("DoggyDrop",mail.Html);Assert.Contains("DoggyDrop",mail.Text);Assert.DoesNotContain("<script>",mail.Html);
        Assert.DoesNotContain("<img",mail.Html);Assert.DoesNotContain("\r",mail.Subject);Assert.DoesNotContain("\n",mail.Subject);
        Assert.Contains("https://doggydrop.example/",mail.Html);Assert.Contains("https://doggydrop.example/",mail.Text);
        Assert.Contains("&lt;",mail.Html);Assert.Contains("Nastavitve",mail.Text);Assert.DoesNotContain("utm_",mail.Html);
        var capture=Environment.GetEnvironmentVariable("DOGGYDROP_EMAIL_CAPTURE");
        if(!string.IsNullOrEmpty(capture)){Directory.CreateDirectory(capture);File.WriteAllText(Path.Combine(capture,$"template-{type}.html"),mail.Html);}
    }
    [Theory][InlineData(ActivityEmailType.BinApproved)][InlineData(ActivityEmailType.PhotoApproved)][InlineData(ActivityEmailType.LocationApproved)]
    public void UnavailableTargetUsesHistory(ActivityEmailType type) {
        var mail=Templates().Render(new NotificationOutbox{Type=type,BinId=1},null,false);
        Assert.DoesNotContain("?binId",mail.Html);Assert.Contains(type==ActivityEmailType.BinApproved?"/Map/MyBins":"/BinContributions/Mine",mail.Text);
    }
    [Theory][InlineData(0,1)][InlineData(99,1)][InlineData(1,0)][InlineData(1,2)]
    public void UnknownPayloadFailsClosed(int type,int version)=>Assert.Throws<InvalidOperationException>(()=>Templates().Render(new(){Type=(ActivityEmailType)type,PayloadVersion=version},null,true));

    [Fact] public async Task QueueIsAtomicAndDeduplicated() => await CheckAtomic(null);
    internal static async Task CheckAtomic(DbContextOptions<ApplicationDbContext>? options) {
        await using var f=await PrivacyTestDatabase.Create(options);var db=f.Db;await Seed(db);
        await using(var tx=await db.Database.BeginTransactionAsync()) {await Queue(db);await tx.RollbackAsync();}
        db.ChangeTracker.Clear();Assert.Empty(await db.NotificationOutbox.ToListAsync());
        await Queue(db);await Queue(db);Assert.Single(await db.NotificationOutbox.ToListAsync());
        db.NotificationOutbox.Add(new(){RecipientUserId="a",Type=ActivityEmailType.BinApproved,EventKey="Bin:1:BinApproved",CreatedAt=DateTime.UtcNow,NextAttemptAt=DateTime.UtcNow});
        await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
    }
    [Theory][InlineData(false,true)][InlineData(true,false)]
    public async Task QueueRejectsUnconfirmedOrOptedOut(bool confirmed,bool enabled) {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);
        await f.Db.Users.Where(u=>u.Id=="a").ExecuteUpdateAsync(s=>s.SetProperty(u=>u.EmailConfirmed,confirmed));
        f.Db.NotificationPreferences.Add(new(){UserId="a",ContributionUpdates=enabled});await f.Db.SaveChangesAsync();
        await Queue(f.Db);Assert.Empty(await f.Db.NotificationOutbox.ToListAsync());
    }
    [Fact] public async Task DeliveryUsesCurrentConfirmedAddress() => await CheckAddress(null);
    internal static async Task CheckAddress(DbContextOptions<ApplicationDbContext>? options) {
        await using var f=await PrivacyTestDatabase.Create(options);await Seed(f.Db);await Queue(f.Db);
        await f.Db.Users.Where(u=>u.Id=="a").ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Email,"changed@example.test"));
        var mail=new Mail();await Delivery(f.Db,mail,new()).RunBatchAsync();Assert.Equal("changed@example.test",Assert.Single(mail.Sent).Recipient);
        Assert.Equal(EmailDeliveryStatus.Sent,(await f.Db.NotificationOutbox.SingleAsync()).Status);
        await Delivery(f.Db,mail,new()).RunBatchAsync();Assert.Single(mail.Sent);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task LatestPreferenceOrConfirmationSuppressesQueuedWork(bool preference) {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);await Queue(f.Db);
        if(preference){f.Db.NotificationPreferences.Add(new(){UserId="a",ContributionUpdates=false});await f.Db.SaveChangesAsync();}
        else await f.Db.Users.Where(u=>u.Id=="a").ExecuteUpdateAsync(s=>s.SetProperty(u=>u.EmailConfirmed,false));
        var mail=new Mail();await Delivery(f.Db,mail,new()).RunBatchAsync();Assert.Empty(mail.Sent);
        Assert.Equal(EmailDeliveryStatus.Suppressed,(await f.Db.NotificationOutbox.SingleAsync()).Status);
    }
    [Fact] public async Task RetriesBackoffAndManualCap() => await CheckRetries(null);
    [Theory][InlineData("address")][InlineData("preference")][InlineData("deletion")]
    public async Task RecipientChangesDuringRenderingAreRechecked(string change) {
        var hook=new BeforeBinRead();await using var f=await PrivacyTestDatabase.Create(interceptor:hook);
        await Seed(f.Db);await Queue(f.Db);
        hook.Run=async()=>{
            await using var other=new ApplicationDbContext(f.Options);
            if(change=="address")await other.Users.Where(u=>u.Id=="a").ExecuteUpdateAsync(s=>s.SetProperty(u=>u.Email,"latest@example.test"));
            else if(change=="preference"){other.NotificationPreferences.Add(new(){UserId="a",ContributionUpdates=false});await other.SaveChangesAsync();}
            else await other.NotificationOutbox.ExecuteDeleteAsync();
        };
        var mail=new Mail();await Delivery(f.Db,mail,new()).RunBatchAsync();
        if(change=="address")Assert.Equal("latest@example.test",Assert.Single(mail.Sent).Recipient);else Assert.Empty(mail.Sent);
    }
    private sealed class BeforeBinRead:DbCommandInterceptor {
        public Func<Task>? Run;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,CommandEventData data,InterceptionResult<DbDataReader> result,CancellationToken ct=default) {
            if(Run!=null&&command.CommandText.Contains("FROM \"TrashBins\"")){var run=Run;Run=null;await run();}return result;
        }
    }
    [Fact] public async Task UnknownStoredPayloadNeverCallsTransport() {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);await Queue(f.Db);
        await f.Db.NotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.PayloadVersion,99));var mail=new Mail();
        await Delivery(f.Db,mail,new()).RunBatchAsync();Assert.Empty(mail.Sent);
        Assert.Equal(EmailFailure.Rendering,(await f.Db.NotificationOutbox.SingleAsync()).Failure);
    }
    internal static async Task CheckRetries(DbContextOptions<ApplicationDbContext>? options) {
        await using var f=await PrivacyTestDatabase.Create(options);await Seed(f.Db);await Queue(f.Db);
        var mail=new Mail{Outcome=EmailFailure.Transient};var clock=new Clock();var worker=Delivery(f.Db,mail,clock);
        for(var attempt=1;attempt<=5;attempt++) {
            Assert.Equal(1,await worker.RunBatchAsync());var row=await f.Db.NotificationOutbox.AsNoTracking().SingleAsync();
            Assert.Equal(attempt,row.AttemptCount);Assert.Equal(0,await worker.RunBatchAsync());
            clock.Now=new DateTimeOffset(DateTime.SpecifyKind(row.NextAttemptAt,DateTimeKind.Utc));
        }
        Assert.Equal(EmailDeliveryStatus.Failed,(await f.Db.NotificationOutbox.AsNoTracking().SingleAsync()).Status);
        for(var attempt=6;attempt<=10;attempt++) {
            var id=await f.Db.NotificationOutbox.Select(n=>n.Id).SingleAsync();
            Assert.Equal(1,await NotificationDelivery.RetryAsync(f.Db,id,clock.Now.UtcDateTime));Assert.Equal(0,await NotificationDelivery.RetryAsync(f.Db,id,clock.Now.UtcDateTime));
            Assert.Equal(1,await worker.RunBatchAsync());Assert.Equal(attempt,(await f.Db.NotificationOutbox.AsNoTracking().SingleAsync()).AttemptCount);
        }
        Assert.Equal(0,await NotificationDelivery.RetryAsync(f.Db,(await f.Db.NotificationOutbox.SingleAsync()).Id,clock.Now.UtcDateTime));
        Assert.Equal(10,mail.Sent.Count);
    }
    [Theory][InlineData(EmailFailure.Configuration)][InlineData(EmailFailure.Recipient)][InlineData(EmailFailure.Rendering)][InlineData(EmailFailure.Disabled)]
    public async Task PermanentFailureDoesNotRetry(EmailFailure failure) {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);await Queue(f.Db);var mail=new Mail{Outcome=failure};
        var w=Delivery(f.Db,mail,new());await w.RunBatchAsync();Assert.Equal(0,await w.RunBatchAsync());Assert.Equal(failure,(await f.Db.NotificationOutbox.SingleAsync()).Failure);
    }
    [Fact] public async Task StaleClaimRecoveryFencesOldWorker() => await CheckLease(null);
    internal static async Task CheckLease(DbContextOptions<ApplicationDbContext>? options) {
        await using var f=await PrivacyTestDatabase.Create(options);await Seed(f.Db);await Queue(f.Db);var clock=new Clock();var mail=new Mail();
        var w=Delivery(f.Db,mail,clock);var old=(await w.ClaimAsync())!;Assert.Null(await w.ClaimAsync());
        clock.Now+=NotificationDelivery.LeaseDuration+TimeSpan.FromSeconds(1);var current=(await w.ClaimAsync())!;
        Assert.NotEqual(old.LeaseToken,current.LeaseToken);await w.DeliverAsync(old);Assert.Empty(mail.Sent);await w.DeliverAsync(current);Assert.Single(mail.Sent);
    }
    [Fact] public async Task ExpiredFinalAttemptDoesNotStick() {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);await Queue(f.Db);
        await f.Db.NotificationOutbox.ExecuteUpdateAsync(s=>s.SetProperty(n=>n.AttemptCount,5).SetProperty(n=>n.Status,EmailDeliveryStatus.Processing).SetProperty(n=>n.LeaseUntil,DateTime.UtcNow.AddMinutes(-1)));
        var mail=new Mail();await Delivery(f.Db,mail,new()).RunBatchAsync();Assert.Empty(mail.Sent);Assert.Equal(EmailDeliveryStatus.Failed,(await f.Db.NotificationOutbox.SingleAsync()).Status);
    }
    [Fact] public async Task ProviderExceptionDoesNotPersistSensitiveText() {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);await Queue(f.Db);var mail=new Mail{Throw=true};
        await Delivery(f.Db,mail,new()).RunBatchAsync();var row=await f.Db.NotificationOutbox.AsNoTracking().SingleAsync();
        Assert.Equal(EmailFailure.Transient,row.Failure);Assert.DoesNotContain("synthetic-secret",JsonSerializer.Serialize(row));
    }
    [Fact] public async Task DeleteAccountRemovesEveryOutboxStateAndPreferences() => await CheckDeletion(null);
    internal static async Task CheckDeletion(DbContextOptions<ApplicationDbContext>? options) {
        await using var f=await PrivacyTestDatabase.Create(options);await Seed(f.Db);
        foreach(var status in Enum.GetValues<EmailDeliveryStatus>()) f.Db.NotificationOutbox.Add(new(){RecipientUserId="a",Type=ActivityEmailType.BinApproved,Status=status,EventKey=status.ToString(),CreatedAt=DateTime.UtcNow,NextAttemptAt=DateTime.UtcNow,LeaseUntil=DateTime.UtcNow.AddMinutes(-1)});
        f.Db.NotificationPreferences.Add(new(){UserId="a"});await f.Db.SaveChangesAsync();f.Db.ChangeTracker.Clear();
        Assert.True((await new AccountDataDeletion(f.Db,f.Users,new NoMedia()).DeleteAsync(await f.Db.Users.SingleAsync(u=>u.Id=="a"))).Succeeded);
        Assert.Empty(await f.Db.NotificationOutbox.ToListAsync());Assert.Empty(await f.Db.NotificationPreferences.ToListAsync());
        var mail=new Mail();await Delivery(f.Db,mail,new()).RunBatchAsync();Assert.Empty(mail.Sent);Assert.True(await f.Db.Users.AnyAsync(u=>u.Id=="b"));
    }
    [Fact] public async Task ExportIncludesEffectivePreferenceAndOnlyOwnedSafeHistory() => await CheckExport(null);
    internal static async Task CheckExport(DbContextOptions<ApplicationDbContext>? options) {
        await using var f=await PrivacyTestDatabase.Create(options);await Seed(f.Db);await Queue(f.Db);
        await ActivityEmails.QueueAsync(f.Db,ActivityEmailType.BinRejected,"b",1);await f.Db.SaveChangesAsync();
        using var output=new MemoryStream();await new PersonalDataExport(f.Db).WriteAsync("a",output);using var json=JsonDocument.Parse(output.ToArray());
        Assert.True(json.RootElement.GetProperty("EmailPreferences").GetProperty("ContributionUpdates").GetBoolean());
        Assert.False(json.RootElement.GetProperty("EmailPreferences").GetProperty("Marketing").GetBoolean());
        var row=Assert.Single(json.RootElement.GetProperty("ActivityEmailHistory").EnumerateArray());
        Assert.Equal(new[]{"Type","Status","CreatedAt","SentAt"},row.EnumerateObject().Select(p=>p.Name));
    }
    [Theory][InlineData("Development")][InlineData("Testing")]
    public async Task NonProductionTransportNeverSends(string env) {
        var t=new SmtpEmailTransport(Options.Create(new EmailSettings{SmtpServer="smtp.invalid",SmtpPort=587,SmtpUser="synthetic",SmtpPass="synthetic-only",SenderEmail="sender@example.test"}),new TestEnvironment{EnvironmentName=env});
        Assert.Equal(EmailFailure.Disabled,await t.SendAsync("recipient@example.test",new("test","test","test"),default));
    }
    [Fact] public async Task MissingSmtpConfigurationFailsWithoutNetwork() {
        var t=new SmtpEmailTransport(Options.Create(new EmailSettings()),new TestEnvironment{EnvironmentName="Production"});
        Assert.Equal(EmailFailure.Configuration,await t.SendAsync("recipient@example.test",new("test","test","test"),default));
    }
    [Fact] public async Task IdentityAdapterRemainsIndependentAndNonThrowing() {
        var mail=new Mail{Throw=true};await new EmailSender(mail).SendEmailAsync("owner@example.test","security","synthetic-token");Assert.Single(mail.Sent);
    }
    [Fact] public async Task CancellationLeavesRecoverableLease() {
        await using var f=await PrivacyTestDatabase.Create();await Seed(f.Db);await Queue(f.Db);var mail=new Mail();var worker=Delivery(f.Db,mail,new());
        await worker.ClaimAsync();using var ct=new CancellationTokenSource();ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>worker.ClaimAsync(ct.Token));
        Assert.Equal(EmailDeliveryStatus.Processing,(await f.Db.NotificationOutbox.SingleAsync()).Status);Assert.Empty(mail.Sent);
    }
    private sealed class NoMedia : IUserMediaCleanup {public Task CleanupAsync(IEnumerable<string?> urls)=>Task.CompletedTask;}
}
