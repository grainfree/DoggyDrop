using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinImportHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-import-http-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;
    private readonly ImportClock clock = new();
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj"))) root=root.Parent;
        Assert.NotNull(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(PlacesController).Assembly.GetName().Name,
            ContentRootPath=Path.Combine(root.FullName,"DoggyDrop"),EnvironmentName="Testing",Args=[] });
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddSingleton<BinImportSessions>();builder.Services.AddScoped<BinImportService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=> { o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test"; })
            .AddScheme<AuthenticationSchemeOptions,TestUser>("Test",_=>{});
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PlacesController).Assembly);
        builder.Services.AddRazorPages().AddApplicationPart(typeof(PlacesController).Assembly);
        app=builder.Build();app.UseRouting();app.UseAuthentication();app.UseAuthorization();
        app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");app.MapRazorPages();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id="admin",UserName="Admin" },new ApplicationUser { Id="other",UserName="Other" },new ApplicationUser { Id="user",UserName="User" });
            db.DataSources.Add(new DataSource { Name="Občina ČŠŽ",Type=DataSourceType.Municipality,DataDate=new DateOnly(2026,8,31) });await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string? user=null)
    {
        var client=new HttpClient(new HttpClientHandler { AllowAutoRedirect=false }) { BaseAddress=new Uri(app.Urls.Single()) };
        if(user!=null)client.DefaultRequestHeaders.Add("X-Test-User",user);return client;
    }
    private static string Field(string html,string name)=>WebUtility.HtmlDecode(Regex.Match(html,$"name=\"{name}\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static async Task<string> Page(HttpClient client,string path)
    { var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);return await response.Content.ReadAsStringAsync(); }
    private static FormUrlEncodedContent Form(string token,params (string Key,string Value)[] fields)=>new(
        fields.Prepend(("__RequestVerificationToken",token)).Select(x=>new KeyValuePair<string,string>(x.Item1,x.Item2)));
    private static async Task<string> Upload(HttpClient client,string token,string csv,int sourceId=1,string filename="municipal.csv")
    {
        using var form=new MultipartFormDataContent();form.Add(new StringContent(token),"__RequestVerificationToken");
        form.Add(new StringContent(sourceId.ToString()),"sourceId");form.Add(new StringContent(";"),"delimiter");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)),"file",filename);
        var response=await client.PostAsync("/AdminBinImport/Upload",form);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        return response.Headers.Location!.OriginalString.Split('/').Last();
    }
    private static async Task<string> Map(HttpClient client,string token,string id)
    {
        var response=await client.PostAsync($"/AdminBinImport/Map/{id}",Form(token,("version","0"),("latitude","0"),("longitude","1"),("name","2")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);return await Page(client,$"/AdminBinImport/Preview/{id}");
    }
    [Fact]
    public async Task EveryImporterEndpointRequiresAdminAndEveryPostRequiresAntiforgery()
    {
        using var anon=Client();using var user=Client("user");using var admin=Client("admin");
        foreach(var action in new[]{"Index","Map/id","Preview/id","Confirm/id","Result/id"})
        { Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("/AdminBinImport/"+action)).StatusCode);Assert.Equal(HttpStatusCode.Forbidden,(await user.GetAsync("/AdminBinImport/"+action)).StatusCode); }
        foreach(var action in new[]{"Upload","Map","Select","Prepare","Apply","Refresh","Cancel"})
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync("/AdminBinImport/"+action,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.PostAsync("/AdminBinImport/"+action,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminBinImport/"+action,Form(""))).StatusCode);
        }
    }
    [Fact]
    public async Task UploadRejectsMissingSourceWrongFormatEncodingAndExcessiveSize()
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminBinImport"),"__RequestVerificationToken");
        foreach(var (name,source,bytes) in new[] {
            ("missing-source.csv","",Encoding.UTF8.GetBytes("lat;lon\n46;15")),
            ("valid.csv","999",Encoding.UTF8.GetBytes("lat;lon\n46;15")),
            ("wrong.xlsx","1",Encoding.UTF8.GetBytes("lat;lon\n46;15")),
            ("legacy.csv","1",new byte[]{0x9a,0x3b,0x31}),
            ("broken.csv","1",Encoding.UTF8.GetBytes("lat;lon\n\"46;15")) })
        {
            using var form=new MultipartFormDataContent();form.Add(new StringContent(token),"__RequestVerificationToken");
            form.Add(new StringContent(source),"sourceId");form.Add(new StringContent(";"),"delimiter");form.Add(new ByteArrayContent(bytes),"file",name);
            var response=await admin.PostAsync("/AdminBinImport/Upload",form);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("validation-summary-errors",await response.Content.ReadAsStringAsync());
        }
        using var huge=new MultipartFormDataContent();huge.Add(new StringContent(token),"__RequestVerificationToken");huge.Add(new StringContent("1"),"sourceId");
        huge.Add(new ByteArrayContent(new byte[BinImportCsv.MaxBytes+65537]),"file","huge.csv");
        using var request=new HttpRequestMessage(HttpMethod.Post,"/AdminBinImport/Upload") { Content=huge };
        request.Headers.ExpectContinue=true; // Let Kestrel reject Content-Length before the client streams the oversized body.
        var rejected=await admin.SendAsync(request);
        Assert.Contains(rejected.StatusCode,new[]{HttpStatusCode.BadRequest,HttpStatusCode.RequestEntityTooLarge});
        await using var scope=app.Services.CreateAsyncScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.ToListAsync());
    }
    [Fact]
    public async Task PreviewRequiresMappingAndConfirmationCannotTamperOrReplayRows()
    {
        using var admin=Client("admin");using var other=Client("other");
        var index=await Page(admin,"/AdminBinImport");await Capture("upload",index);var token=Field(index,"__RequestVerificationToken");
        var id=await Upload(admin,token,"lat;lon;name;unused\n46;15;=SUM(1);SECRET-UNUSED\n46.00001;15;Duplicate;SECRET-UNUSED\nNaN;15;Invalid;SECRET-UNUSED\n47;15;ČŠŽ;SECRET-UNUSED",filename:"<municipal>.csv");
        var map=await Page(admin,$"/AdminBinImport/Map/{id}");await Capture("mapping",map);
        Assert.Contains("&lt;municipal&gt;.csv",map);Assert.DoesNotContain("SECRET-UNUSED",map);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/AdminBinImport/Map/{id}")).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.TrashBins.ToListAsync());
        var preview=await Map(admin,token,id);await Capture("preview",preview);
        Assert.Contains("POSSIBLE_DUPLICATE",preview);Assert.Contains("INVALID",preview);
        var decoded=WebUtility.HtmlDecode(preview);
        Assert.Contains("Skupaj: 4 · Pripravljeno: 2 · Možni dvojniki: 1 · Neveljavno: 1",decoded);
        Assert.Contains("Manjka veljavna številčna širina ali dolžina",decoded);
        var session=app.Services.GetRequiredService<BinImportSessions>().Find(id,"admin")!;
        Assert.Null(session.Csv);Assert.Equal(new[]{2,5},session.Selected.Order());Assert.Equal(new[]{2,3,4,5},session.Rows.Select(r=>r.Number));
        Assert.Empty(await db.TrashBins.ToListAsync());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","3")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","4")))).StatusCode);
        var selected=await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","2"),("rows","5"),("proceed","true")));
        Assert.Equal(HttpStatusCode.Redirect,selected.StatusCode);
        var confirm=await Page(admin,$"/AdminBinImport/Confirm/{id}");await Capture("confirm",confirm);
        var otherToken=Field(await Page(other,"/AdminBinImport"),"__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync($"/AdminBinImport/Apply/{id}",Form(otherToken,("version","2")))).StatusCode);
        var applied=await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","2"),("sourceId","999"),("latitude","90"),("rows","3"),("UserId","admin")));
        Assert.Equal(HttpStatusCode.Redirect,applied.StatusCode);
        var resultPage=await Page(admin,$"/AdminBinImport/Result/{id}");await Capture("result",resultPage);
        Assert.Contains("Skupaj prebranih vrstic: 4",WebUtility.HtmlDecode(resultPage));
        var bins=await db.TrashBins.AsNoTracking().OrderBy(b=>b.Id).ToListAsync();Assert.Equal(2,bins.Count);
        Assert.Equal(46,bins[0].Latitude);Assert.Equal(47,bins[1].Latitude);Assert.All(bins,b=>{Assert.Equal(1,b.DataSourceId);Assert.Null(b.UserId);});
        await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","2")));Assert.Equal(2,await db.TrashBins.CountAsync());
    }
    [Fact]
    public async Task PaginationSelectionsAndOldConfirmationsAreValidated()
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminBinImport"),"__RequestVerificationToken");
        var csv="lat;lon;name\n"+string.Join('\n',Enumerable.Range(0,101).Select(i=>$"{(46+i*.001).ToString(System.Globalization.CultureInfo.InvariantCulture)};15;Koš"));
        var id=await Upload(admin,token,csv);var preview=await Map(admin,token,id);
        Assert.Equal(100,Regex.Matches(preview,"name=\"rows\"").Count);
        var second=await Page(admin,$"/AdminBinImport/Preview/{id}?page=2");Assert.Single(Regex.Matches(second,"name=\"rows\""));
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","102")))).StatusCode);
        await admin.PostAsync($"/AdminBinImport/Prepare/{id}",Form(token,("version","1")));
        await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1"))); // Deselect first page; page 2 remains selected.
        Assert.Single(app.Services.GetRequiredService<BinImportSessions>().Find(id,"admin")!.Selected);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1")))).StatusCode);
    }
    [Fact]
    public async Task SourceDeletionAndNewDuplicateAfterPreviewAbortWithoutPartialInsert()
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminBinImport"),"__RequestVerificationToken");
        var id=await Upload(admin,token,"lat;lon;name\n46;15;First\n47;15;Second");await Map(admin,token,id);
        await admin.PostAsync($"/AdminBinImport/Prepare/{id}",Form(token,("version","1")));
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.TrashBins.Add(new TrashBin { Name="Concurrent",Latitude=47,Longitude=15 });await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Equal(1,await db.TrashBins.CountAsync());
        await admin.PostAsync($"/AdminBinImport/Refresh/{id}",Form(token,("version","2")));
        Assert.Single(app.Services.GetRequiredService<BinImportSessions>().Find(id,"admin")!.Selected);
        await admin.PostAsync($"/AdminBinImport/Prepare/{id}",Form(token,("version","3")));
        await db.DataSources.ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","3")))).StatusCode);
        Assert.Equal(1,await db.TrashBins.CountAsync());
    }
    [Fact]
    public async Task ExpiredConfirmationCannotInsertOrRevealPreview()
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminBinImport"),"__RequestVerificationToken");
        var id=await Upload(admin,token,"lat;lon;name\n46;15;Koš");await Map(admin,token,id);
        await admin.PostAsync($"/AdminBinImport/Prepare/{id}",Form(token,("version","1")));
        clock.Now=clock.Now.AddMinutes(31);
        var rejected=await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","1")));
        Assert.Equal(HttpStatusCode.NotFound,rejected.StatusCode);Assert.Contains("potekel",await rejected.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/AdminBinImport/Preview/{id}")).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();Assert.Empty(await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.ToListAsync());
    }
    [Fact]
    public async Task OnlySelectedReadyRowsBecomePublicAndNoImportMetadataLeaks()
    {
        using var admin=Client("admin");using var anon=Client();
        var token=Field(await Page(admin,"/AdminBinImport"),"__RequestVerificationToken");
        const string csv="lat;lon;name;unused\n46;15;Public selected;PRIVATE-RAW\n47;15;Not selected;PRIVATE-RAW\n46.00001;15;Duplicate;PRIVATE-RAW\nNaN;15;Invalid;PRIVATE-RAW";
        var id=await Upload(admin,token,csv,filename:"../../private-filename.csv");
        var mapping=await Page(admin,$"/AdminBinImport/Map/{id}");Assert.DoesNotContain("../../",mapping);Assert.Contains("private-filename.csv",mapping);
        await Map(admin,token,id);
        await admin.PostAsync($"/AdminBinImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","2"),("proceed","true")));
        await admin.PostAsync($"/AdminBinImport/Apply/{id}",Form(token,("version","2")));
        var result=WebUtility.HtmlDecode(await Page(admin,$"/AdminBinImport/Result/{id}"));
        Assert.Contains("Uvoženih 1 košev",result);Assert.Contains("Skupaj prebranih vrstic: 4",result);
        Assert.Contains("Neizbrane pripravljene vrstice: 1",result);Assert.Contains("Izključeni možni dvojniki: 1",result);Assert.Contains("Neveljavno: 1",result);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source=await db.DataSources.SingleAsync();source.ContactName="PRIVATE-CONTACT";source.ContactEmail="private@example.com";source.Notes="PRIVATE-NOTES";
        source.WebsiteUrl="https://private-source.example/";await db.SaveChangesAsync();
        var publicJson=await Page(anon,"/api/trashbins/nearby?latitude=46&longitude=15");
        using var payload=JsonDocument.Parse(publicJson);var bin=Assert.Single(payload.RootElement.EnumerateArray());
        Assert.Equal("Public selected",bin.GetProperty("name").GetString());Assert.Equal(46,bin.GetProperty("latitude").GetDouble());
        foreach(var secret in new[]{"PRIVATE-RAW","PRIVATE-CONTACT","PRIVATE-NOTES","private@example.com","private-source.example","private-filename.csv",id,"Admin","INVALID","PossibleDuplicate"})
            Assert.DoesNotContain(secret,publicJson);
        var saved=Assert.Single(await db.TrashBins.AsNoTracking().ToListAsync());Assert.True(saved.IsApproved);Assert.NotNull(saved.ApprovedAt);Assert.Equal(1,saved.DataSourceId);
        // Reupload the same dataset: the previously imported row is excluded from the new default selection.
        var second=await Upload(admin,token,csv);await Map(admin,token,second);
        var session=app.Services.GetRequiredService<BinImportSessions>().Find(second,"admin")!;
        Assert.DoesNotContain(2,session.Selected);Assert.Contains(3,session.Selected);Assert.Single(session.Selected);
        Assert.Equal(1,await db.TrashBins.CountAsync());
    }
    private static async Task Capture(string name,string html)
    {
        var output=Environment.GetEnvironmentVariable("DOGGYDROP_IMPORT_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;
        Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,name+".html"),html);
    }
    public async Task DisposeAsync() {if(app!=null)await app.DisposeAsync();if(File.Exists(database))File.Delete(database);}
    private sealed class ImportClock : TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user=Request.Headers["X-Test-User"].ToString();if(user is not ("admin" or "other" or "user"))return Task.FromResult(AuthenticateResult.NoResult());
            var identity=new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user)],IdentityConstants.ApplicationScheme);
            if(user!="user")identity.AddClaim(new Claim(ClaimTypes.Role,"Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));
        }
    }
}
