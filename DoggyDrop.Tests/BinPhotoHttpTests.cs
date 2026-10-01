using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
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

public sealed class BinPhotoHttpTests : IAsyncLifetime
{
    private readonly string database=Path.Combine(Path.GetTempPath(),"doggydrop-photo-http-"+Guid.NewGuid().ToString("N")+".db");
    private WebApplication app=null!;private readonly FakeBinPhotoStorage storage=new();
    public async Task InitializeAsync()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);while(root!=null&&!File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj")))root=root.Parent;
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions{ApplicationName=typeof(MapController).Assembly.GetName().Name,ContentRootPath=Path.Combine(root!.FullName,"DoggyDrop"),EnvironmentName="Testing",Args=[]});
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton<IBinPhotoStorage>(storage);builder.Services.AddScoped<IBinPhotoReferences,BinPhotoReferences>();builder.Services.AddScoped<BinPhotoRotationService>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=>{o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test";}).AddScheme<AuthenticationSchemeOptions,TestUser>("Test",_=>{});
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(MapController).Assembly).AddControllersAsServices();
        builder.Services.AddRazorPages();
        builder.Services.AddTransient(sp=>new MapController(sp.GetRequiredService<ApplicationDbContext>(),sp.GetRequiredService<IWebHostEnvironment>(),sp.GetRequiredService<UserManager<ApplicationUser>>(),new FailedUpload(),null!,null!,null!,null!,null!,null!,null!,null!));
        app=builder.Build();app.UseRouting();app.UseAuthentication();app.UseAuthorization();app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");app.MapRazorPages();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.Users.Add(new ApplicationUser{Id="admin",UserName="Admin"});
            db.TrashBins.AddRange(new TrashBin{Name="Photo",ImageUrl=BinPhotoRotationTests.Original,IsApproved=true,Latitude=46,Longitude=15},new TrashBin{Name="No photo",Latitude=46.1,Longitude=15},new TrashBin{Name="External",ImageUrl="https://external.test/photo.jpg"});await db.SaveChangesAsync();
        }
        await app.StartAsync();
    }
    private HttpClient Client(string? user=null){var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(app.Urls.Single())};if(user!=null)client.DefaultRequestHeaders.Add("X-Test-User",user);return client;}
    private static string Token(string html)=>WebUtility.HtmlDecode(Regex.Match(html,"name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value);
    private static FormUrlEncodedContent Form(string token,params (string Key,string Value)[] values)=>new(values.Prepend(("__RequestVerificationToken",token)).Select(v=>new KeyValuePair<string,string>(v.Item1,v.Item2)));
    [Fact]
    public async Task MutationIsAdminOnlyPostOnlyAndAntiforgeryProtected()
    {
        using var anon=Client();using var user=Client("user");using var admin=Client("admin");
        Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync("/AdminBinPhotos/Rotate",Form("",("id","1"),("operation","right")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await user.PostAsync("/AdminBinPhotos/Rotate",Form("",("id","1"),("operation","right")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminBinPhotos/Rotate",Form("",("id","1"),("operation","right")))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed,(await admin.GetAsync("/AdminBinPhotos/Rotate?id=1&operation=right")).StatusCode);
        Assert.Empty(storage.Operations);
    }
    [Fact]
    public async Task RealEditPageHasSeparateAccessibleRotationFormsAndRejectsMissingImages()
    {
        using var admin=Client("admin");var html=await admin.GetStringAsync("/Map/Edit/1");var token=Token(html);
        Assert.NotEmpty(token);var decoded=WebUtility.HtmlDecode(html);Assert.Contains("Zavrti levo",decoded);Assert.Contains("Zavrti desno",decoded);Assert.Contains("Obrni za 180°",decoded);
        Assert.Contains("id=\"bin-photo-rotation\"",html);
        var output=Environment.GetEnvironmentVariable("DOGGYDROP_BIN_PHOTO_REVIEW_OUTPUT");if(!string.IsNullOrEmpty(output)){Directory.CreateDirectory(output);await File.WriteAllTextAsync(Path.Combine(output,"edit.html"),html);}
        foreach(var id in new[]{2,3})
        {
            var page=await admin.GetStringAsync($"/Map/Edit/{id}");Assert.DoesNotContain("id=\"bin-photo-rotation\"",page);
            Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminBinPhotos/Rotate",Form(token,("id",id.ToString()),("operation","right")))).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,(await admin.PostAsync("/AdminBinPhotos/Rotate",Form(token,("id","999"),("operation","right")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminBinPhotos/Rotate",Form(token,("id","1"),("operation","arbitrary")))).StatusCode);Assert.Empty(storage.Created);
    }
    [Fact]
    public async Task BrowserCannotChooseAssetAndAlreadyApprovedBinCannotTriggerDelayedApproval()
    {
        using var admin=Client("admin");var token=Token(await admin.GetStringAsync("/Map/Edit/1"));
        var result=await admin.PostAsync("/AdminBinPhotos/Rotate",Form(token,("id","1"),("operation","right"),("url","https://attacker.test/"),("publicId","unrelated"),("path","../secret")));
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);Assert.Equal("/Map/Edit/1",result.Headers.Location!.OriginalString);Assert.Equal(BinPhotoRotationTests.Original,storage.Operations.Single().Url);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/Map/Approve/1",Form(token))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(storage.Created.Single(),(await db.TrashBins.FindAsync(1))!.ImageUrl);Assert.Empty(await db.UserNotifications.ToListAsync());
    }
    [Fact]
    public async Task PendingAnonymousSubmissionKeepsCorrectedPhotoWhenApproved()
    {
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();var bin=(await db.TrashBins.FindAsync(2))!;
            bin.ImageUrl=BinPhotoRotationTests.Original;await db.SaveChangesAsync();
        }
        using var admin=Client("admin");var token=Token(await admin.GetStringAsync("/Map/Edit/2"));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminBinPhotos/Rotate",Form(token,("id","2"),("operation","left")))).StatusCode);
        using var approvalScope=app.Services.CreateScope();var approvalBin=await approvalScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.AsNoTracking().SingleAsync(b=>b.Id==2);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/Map/Approve/2",Form(token,("snapshot",BinCommunityRules.Snapshot(approvalBin))))).StatusCode);
        await using var verify=app.Services.CreateAsyncScope();var saved=await verify.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.FindAsync(2);
        Assert.True(saved!.IsApproved);Assert.NotNull(saved.ApprovedAt);Assert.Equal(storage.Created.Single(),saved.ImageUrl);Assert.Single(storage.Operations);
    }
    [Fact]
    public async Task PublicAddRemainsPublicAndFailedPhotoNeverSilentlyCreatesBinOrReplacesOld()
    {
        using var anon=Client();var page=await anon.GetStringAsync("/Map/Add");Assert.DoesNotContain("bin-photo-rotation",page);
        using var upload=new MultipartFormDataContent();upload.Add(new StringContent(Token(page)),"__RequestVerificationToken");upload.Add(new StringContent("New bin"),"Name");upload.Add(new StringContent("46.2"),"Latitude");upload.Add(new StringContent("15"),"Longitude");upload.Add(new ByteArrayContent([1,2,3]),"ImageFile","bad.jpg");
        var result=await anon.PostAsync("/Map/Add",upload);Assert.Equal(HttpStatusCode.OK,result.StatusCode);Assert.Contains("12 MiB",await result.Content.ReadAsStringAsync());
        using var admin=Client("admin");var token=Token(await admin.GetStringAsync("/Map/Edit/1"));
        using var scopeSnapshot=app.Services.CreateScope();var snapshot=BinCommunityRules.Snapshot(scopeSnapshot.ServiceProvider.GetRequiredService<ApplicationDbContext>().TrashBins.Find(1)!);
        using var replacement=new MultipartFormDataContent();replacement.Add(new StringContent(snapshot),"Snapshot");replacement.Add(new StringContent(token),"__RequestVerificationToken");replacement.Add(new StringContent("1"),"Id");replacement.Add(new StringContent("Photo"),"Name");replacement.Add(new StringContent("46"),"Latitude");replacement.Add(new StringContent("15"),"Longitude");replacement.Add(new ByteArrayContent([1,2,3]),"ImageFile","bad.jpg");
        Assert.Equal(HttpStatusCode.OK,(await admin.PostAsync("/Map/Edit",replacement)).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();Assert.Equal(3,await db.TrashBins.CountAsync());Assert.Equal(BinPhotoRotationTests.Original,(await db.TrashBins.FindAsync(1))!.ImageUrl);
    }
    public async Task DisposeAsync(){if(app!=null)await app.DisposeAsync();if(File.Exists(database))File.Delete(database);}
    private sealed class FailedUpload : ICloudinaryService
    {public Task<string?> UploadImageAsync(Microsoft.AspNetCore.Http.IFormFile f)=>throw new NotSupportedException();public Task<string?> UploadTrashBinImageAsync(Microsoft.AspNetCore.Http.IFormFile f)=>Task.FromResult<string?>(null);public Task<string?> UploadWalkImageAsync(Microsoft.AspNetCore.Http.IFormFile f)=>throw new NotSupportedException();}
    private sealed class TestUser(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory l,UrlEncoder e):AuthenticationHandler<AuthenticationSchemeOptions>(o,l,e)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync(){var user=Request.Headers["X-Test-User"].ToString();if(user is not("admin" or "user"))return Task.FromResult(AuthenticateResult.NoResult());var identity=new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user)],IdentityConstants.ApplicationScheme);if(user=="admin")identity.AddClaim(new Claim(ClaimTypes.Role,"Admin"));return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity),Scheme.Name)));}
    }
}
