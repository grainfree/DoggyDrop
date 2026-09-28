using System.Net;
using System.Security.Claims;
using System.Text;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;
using System.Net.Http.Headers;
using SkiaSharp;

namespace DoggyDrop.Tests;

public sealed class BulkPlaceLogoHttpTests : IAsyncLifetime
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-bulk-logo-http-{Guid.NewGuid():N}.db");
    private WebApplication app = null!;
    private readonly ImportClock clock = new();
    private readonly FakeCloud cloud = new();
    public async Task InitializeAsync()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"DoggyDrop","DoggyDrop.csproj"))) root=root.Parent;
        Assert.NotNull(root);
        var builder=WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName=typeof(PlacesController).Assembly.GetName().Name,
            ContentRootPath=Path.Combine(root.FullName,"DoggyDrop"),EnvironmentName="Production",Args=[] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.app";
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddSingleton<IGamificationCalendar,GamificationCalendar>();builder.Services.AddScoped<IWeeklyGoalsService,WeeklyGoalsService>();
        builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddScoped<BulkPlaceLogos>();
        builder.Services.AddSingleton<IPlaceLogoCloudinaryClient>(cloud);builder.Services.AddScoped<IPlaceLogoStorage,CloudinaryPlaceLogoStorage>();
        builder.Services.AddSingleton<IImageOptimizationService,ImageOptimizationService>();builder.Services.AddScoped<IPlaceLogoReferenceReader,PlaceLogoReferenceReader>();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddIdentity<ApplicationUser,IdentityRole>().AddEntityFrameworkStores<ApplicationDbContext>();
        builder.Services.AddAuthentication(o=> { o.DefaultAuthenticateScheme="Test";o.DefaultChallengeScheme="Test"; })
            .AddScheme<AuthenticationSchemeOptions,TestUser>("Test",_=>{});
        builder.Services.AddControllersWithViews().AddApplicationPart(typeof(PlacesController).Assembly).AddControllersAsServices();
        builder.Services.AddRazorPages().AddApplicationPart(typeof(PlacesController).Assembly);
        builder.Services.AddTransient(sp=>new MapController(sp.GetRequiredService<ApplicationDbContext>(),sp.GetRequiredService<IWebHostEnvironment>(),
            sp.GetRequiredService<UserManager<ApplicationUser>>(),null!,null!,null!,null!,null!,null!,null!,null!,null!,placeLogoCloud:new("test")));
        app=builder.Build();app.UseRouting();app.UseAuthentication();app.UseAuthorization();
        app.MapControllerRoute("default","{controller=Map}/{action=Index}/{id?}");app.MapRazorPages();
        await using(var scope=app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(new ApplicationUser { Id="admin",UserName="Admin" },new ApplicationUser { Id="other",UserName="Other" },new ApplicationUser { Id="user",UserName="User" });
            db.DataSources.Add(new DataSource { Name="Synthetic source",Type=DataSourceType.Municipality,DataDate=new DateOnly(2026,8,31) });await db.SaveChangesAsync();
            db.Places.AddRange(Enumerable.Range(1,33).Select(i=>new Place{Id=i,Name="Mr.Pet Test "+i,Category=PlaceCategory.PetShop,Latitude=46,Longitude=15}));
            db.Places.Add(new Place{Id=100,Name="Park",Category=PlaceCategory.DogPark,Latitude=46,Longitude=15});
            db.SavedPlaces.Add(new SavedPlace{PlaceId=1,UserId="admin",SavedAt=DateTime.UtcNow});await db.SaveChangesAsync();
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

    private async Task<(string Token,string Ticket)> Preview(HttpClient client,params int[] ids)
    {
        var list=await Page(client,"/AdminPlaces?q=Mr.Pet");var token=Field(list,"__RequestVerificationToken");
        Assert.Contains("/AdminPlaceLogos/Preview",list);Assert.Contains("formnovalidate",list);
        var response=await client.PostAsync("/AdminPlaceLogos/Preview",Form(token,ids.Select(id=>("Ids",id.ToString())).ToArray()));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=await response.Content.ReadAsStringAsync();
        if(ids.Distinct().Count()==33){await Capture("list",list);await Capture("confirm",html);}
        return (token,Field(html,"token"));
    }
    private static async Task<HttpResponseMessage> Apply(HttpClient client,string token,string ticket,byte[]? bytes=null,string name="logo.png",string type="image/png")
    {
        using var form=new MultipartFormDataContent();form.Add(new StringContent(token),"__RequestVerificationToken");form.Add(new StringContent(ticket),"token");
        var file=BulkPlaceLogoTests.Image();using var copy=new MemoryStream();await file.CopyToAsync(copy);
        var content=new ByteArrayContent(bytes??copy.ToArray());content.Headers.ContentType=new MediaTypeHeaderValue(type);form.Add(content,"file",name);
        foreach(var (key,value) in new[]{("ids","100"),("category","DogBeach"),("LogoUrl","https://evil.invalid/new.webp"),("oldLogoUrl","https://evil.invalid/old.webp"),("storageKey","evil-key"),("publicId","evil-id"),("DataSourceId","999")})
            form.Add(new StringContent(value),key);
        return await client.PostAsync("/AdminPlaceLogos/Apply",form);
    }
    [Fact]
    public async Task AdminAntiforgeryAndPostOnlyAreRequired()
    {
        using var anon=Client();using var user=Client("user");using var admin=Client("admin");
        foreach(var action in new[]{"Preview","Apply"})
        {
            var path="/AdminPlaceLogos/"+action;
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync(path,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.PostAsync(path,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync(path,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.MethodNotAllowed,(await admin.GetAsync(path)).StatusCode);
        }
        Assert.Equal(0,cloud.Uploads);
    }
    [Fact]
    public async Task ThirtyThreeSharedLogosIgnoreBrowserAssetFieldsAndIntegrateNormally()
    {
        using var admin=Client("admin");using var anon=Client();
        var (token,ticket)=await Preview(admin,Enumerable.Range(1,33).Append(1).ToArray());
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0,cloud.Uploads);Assert.All(await db.Places.AsNoTracking().ToListAsync(),p=>Assert.Null(p.LogoUrl));
        var savedBefore=await db.SavedPlaces.AsNoTracking().SingleAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await Apply(admin,token,ticket)).StatusCode);
        Assert.Equal(1,cloud.Uploads);Assert.Empty(cloud.Deleted);Assert.NotNull(cloud.Url);
        using(var codec=SKCodec.Create(new MemoryStream(cloud.Bytes!)))Assert.Equal(SKEncodedImageFormat.Webp,codec.EncodedFormat);
        Assert.All(await db.Places.AsNoTracking().Where(p=>p.Id<100).ToListAsync(),p=>Assert.Equal(cloud.Url,p.LogoUrl));
        Assert.Null((await db.Places.AsNoTracking().SingleAsync(p=>p.Id==100)).LogoUrl);
        var result=await Page(admin,"/AdminPlaces");Assert.Contains("33 lokacij",WebUtility.HtmlDecode(result));await Capture("result",result);
        var publicLogo=PlaceCategories.PublicLogo(PlaceCategory.PetShop,cloud.Url,"test")!;
        foreach(var path in new[]{"/","/Places","/Places/Details/1",SeoMetadata.PlacePath(1,"Mr.Pet Test 1")})
        {
            var html=await Page(anon,path);Assert.Contains(publicLogo,WebUtility.HtmlDecode(html));
            if(path.StartsWith("/lokacije"))Assert.Contains("property=\"og:image\"",html);
        }
        Assert.Contains(publicLogo,WebUtility.HtmlDecode(await Page(admin,"/SavedPlaces")));
        var savedAfter=await db.SavedPlaces.AsNoTracking().SingleAsync();Assert.Equal(savedBefore.SavedAt,savedAfter.SavedAt);
        Assert.Equal(HttpStatusCode.Redirect,(await Apply(admin,token,ticket)).StatusCode);Assert.Equal(1,cloud.Uploads); // stale ticket fails before upload
    }
    [Fact]
    public async Task OwnerExpiryTamperingAndStaleEditRejectBeforeUpload()
    {
        using var admin=Client("admin");using var other=Client("other");var (token,ticket)=await Preview(admin,1,2);
        var otherToken=Field(await Page(other,"/AdminPlaces"),"__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.BadRequest,(await Apply(other,otherToken,ticket)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await Apply(admin,token,ticket+"tampered")).StatusCode);
        clock.Now=clock.Now.AddMinutes(11);Assert.Equal(HttpStatusCode.BadRequest,(await Apply(admin,token,ticket)).StatusCode);
        (token,ticket)=await Preview(admin,1,2);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var changed=await db.Places.FindAsync(2);changed!.UpdatedAt=PlaceUpdates.NextUpdatedAt(changed.UpdatedAt);changed.Name="Other edit";await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await Apply(admin,token,ticket)).StatusCode);Assert.Equal(0,cloud.Uploads);
        Assert.All(await db.Places.AsNoTracking().ToListAsync(),p=>Assert.Null(p.LogoUrl));
    }
    [Fact]
    public async Task BulkInvalidatesSingleEditAndSingleRemovalKeepsOtherSharedReference()
    {
        using var admin=Client("admin");var edit=await Page(admin,"/AdminPlaces/Edit/1");
        var original=Field(edit,"OriginalUpdatedAt");var (token,ticket)=await Preview(admin,1,2);
        await Apply(admin,token,ticket);
        FormUrlEncodedContent EditForm(string version)=>Form(token,("OriginalUpdatedAt",version),("Name","Mr.Pet Test 1"),
            ("Category","PetShop"),("Latitude","46"),("Longitude","15"),("RemoveLogo","true"));
        var stale=await admin.PostAsync("/AdminPlaces/Edit/1",EditForm(original));
        Assert.Equal(HttpStatusCode.OK,stale.StatusCode);Assert.Contains("validation-summary-errors",await stale.Content.ReadAsStringAsync());
        Assert.Empty(cloud.Deleted);
        var current=Field(await Page(admin,"/AdminPlaces/Edit/1"),"OriginalUpdatedAt");
        Assert.NotEqual(original,current);Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminPlaces/Edit/1",EditForm(current))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Null((await db.Places.AsNoTracking().SingleAsync(p=>p.Id==1)).LogoUrl);
        Assert.Equal(cloud.Url,(await db.Places.AsNoTracking().SingleAsync(p=>p.Id==2)).LogoUrl);Assert.Empty(cloud.Deleted);
    }
    [Theory]
    [InlineData("svg")] [InlineData("spoof")] [InlineData("decode")] [InlineData("dimension")]
    public async Task ExistingImagePolicyRejectsInvalidPayloadWithoutRemoteUpload(string kind)
    {
        using var admin=Client("admin");var (token,ticket)=await Preview(admin,1);
        byte[] bytes="<svg/>"u8.ToArray();var name="logo.png";var type="image/png";
        if(kind=="svg"){name="logo.svg";type="image/svg+xml";}
        if(kind=="decode")bytes=[137,80,78,71,13,10,26,10,0,0,0,0];
        if(kind=="dimension")
        {
            using var bitmap=new SKBitmap(4097,1);using var image=SKImage.FromBitmap(bitmap);using var data=image.Encode(SKEncodedImageFormat.Png,100);bytes=data.ToArray();
        }
        Assert.Equal(HttpStatusCode.Redirect,(await Apply(admin,token,ticket,bytes,name,type)).StatusCode);
        Assert.Equal(0,cloud.Uploads);Assert.Empty(cloud.Deleted);
        Assert.Contains("role=\"status\"",await Page(admin,"/AdminPlaces"));
    }
    [Fact]
    public async Task UnsupportedCategoryMissingAndHugeSelectionsAreRejectedAndFilterIsExplicit()
    {
        using var admin=Client("admin");var list=await Page(admin,"/AdminPlaces?q=Mr.Pet");var token=Field(list,"__RequestVerificationToken");
        Assert.DoesNotContain("Izberi lokacijo #100",list);Assert.Equal(33,Regex.Matches(list,"form=\"dataBulkForm\"").Count);
        foreach(var ids in new[]{new[]{1,100},new[]{999},Enumerable.Repeat(1,101).ToArray(),Array.Empty<int>()})
        {
            Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync("/AdminPlaceLogos/Preview",Form(token,ids.Select(id=>("Ids",id.ToString())).ToArray()))).StatusCode);
        }
        Assert.Equal(0,cloud.Uploads);
    }
    private static async Task Capture(string name,string html)
    {
        var directory=Environment.GetEnvironmentVariable("DOGGYDROP_BULK_LOGO_CAPTURE");
        if(!string.IsNullOrEmpty(directory)){Directory.CreateDirectory(directory);await File.WriteAllTextAsync(Path.Combine(directory,name+".html"),html);}
    }

    [Theory]
    [InlineData("version", false)] [InlineData("host", false)] [InlineData("transform", false)] [InlineData("query", false)]
    [InlineData("version", true)] [InlineData("host", true)] [InlineData("transform", true)] [InlineData("query", true)]
    public async Task SinglePlaceReplacementAndRemovalPreserveAliasesUntilLastReference(string kind, bool remove)
    {
        using var admin = Client("admin");
        var raw = BulkPlaceLogoTests.Url('a'); var alias = BulkPlaceLogoTests.Alias(kind);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var p in await db.Places.Where(p => p.Id < 100).ToListAsync()) p.LogoUrl = p.Id == 1 ? raw : alias;
            await db.SaveChangesAsync();
        }
        async Task Edit(int id, bool delete)
        {
            var html = await Page(admin, "/AdminPlaces/Edit/" + id);
            using var form = new MultipartFormDataContent();
            foreach (var (key, value) in new[] {
                ("__RequestVerificationToken", Field(html,"__RequestVerificationToken")),
                ("OriginalUpdatedAt", Field(html,"OriginalUpdatedAt")), ("Name", "Mr.Pet Test " + id),
                ("Category", "PetShop"), ("Latitude", "46"), ("Longitude", "15"), ("RemoveLogo", delete ? "true" : "false") })
                form.Add(new StringContent(value), key);
            if (!delete)
            {
                var image = BulkPlaceLogoTests.Image(); using var bytes = new MemoryStream(); await image.CopyToAsync(bytes);
                var content = new ByteArrayContent(bytes.ToArray()); content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(content, "LogoFile", "logo.png");
            }
            Assert.Equal(HttpStatusCode.Redirect, (await admin.PostAsync("/AdminPlaces/Edit/" + id, form)).StatusCode);
        }
        await Edit(1, remove); Assert.Empty(cloud.Deleted);
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(32, await db.Places.CountAsync(p => p.LogoUrl == alias));
            Assert.Equal(remove ? null : cloud.Url, (await db.Places.AsNoTracking().SingleAsync(p => p.Id == 1)).LogoUrl);
        }
        for (var id = 2; id <= 33; id++)
        {
            await Edit(id, true);
            if (id < 33) Assert.Empty(cloud.Deleted);
        }
        Assert.Equal("doggydrop/places/logos/" + new string('a', 32), Assert.Single(cloud.Deleted));
        Assert.Equal(remove ? 0 : 1, cloud.Uploads);
    }
    private sealed class FakeCloud:IPlaceLogoCloudinaryClient
    {
        public string CloudName=>"test";public int Uploads;public string? Url;public byte[]? Bytes;public List<string> Deleted=[];
        public async Task<PlaceLogoUploadResponse> UploadAsync(Stream content,string publicId)
        {Uploads++;using var memory=new MemoryStream();await content.CopyToAsync(memory);Bytes=memory.ToArray();Url=$"https://res.cloudinary.com/test/image/upload/v1/{publicId}.webp";return new(publicId,Url,true);}
        public Task DeleteAsync(string publicId){Deleted.Add(publicId);return Task.CompletedTask;}
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
