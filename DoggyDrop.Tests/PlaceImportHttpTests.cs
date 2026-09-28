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

namespace DoggyDrop.Tests;

public sealed class PlaceImportHttpTests : IAsyncLifetime
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
            ContentRootPath=Path.Combine(root.FullName,"DoggyDrop"),EnvironmentName="Production",Args=[] });
        builder.Configuration.Sources.Clear(); builder.Configuration.AddInMemoryCollection(); builder.Configuration["Seo:PublicOrigin"]="https://doggydrop.app";
        builder.Logging.ClearProviders();builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddDbContext<ApplicationDbContext>(o=>o.UseSqlite($"Data Source={database};Pooling=False"));
        builder.Services.AddSingleton(new PlaceLogoCloudName("test"));
        builder.Services.AddSingleton<TimeProvider>(clock);builder.Services.AddSingleton<PlaceImportSessions>();builder.Services.AddScoped<PlaceImportService>();
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
    private static async Task<string> Upload(HttpClient client,string token,string csv,int sourceId=1,string filename="places.csv", string? category="PetShop")
    {
        using var form=new MultipartFormDataContent();form.Add(new StringContent(token),"__RequestVerificationToken");
        if(category!=null)form.Add(new StringContent(category),"category");
        form.Add(new StringContent(sourceId.ToString()),"sourceId");form.Add(new StringContent(";"),"delimiter");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)),"file",filename);
        var response=await client.PostAsync("/AdminPlaceImport/Upload",form);Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        return response.Headers.Location!.OriginalString.Split('/').Last();
    }
    private static async Task<string> Map(HttpClient client,string token,string id)
    {
        var response=await client.PostAsync($"/AdminPlaceImport/Map/{id}",Form(token,("version","0"),("name","0"),("latitude","1"),("longitude","2")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);return await Page(client,$"/AdminPlaceImport/Preview/{id}");
    }

    [Fact]
    public async Task EveryEndpointRequiresAdminAndEveryMutationRequiresAntiforgery()
    {
        using var anon=Client();using var user=Client("user");using var admin=Client("admin");
        foreach(var action in new[]{"Index","Map/id","Preview/id","Confirm/id","Result/id"})
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.GetAsync("/AdminPlaceImport/"+action)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.GetAsync("/AdminPlaceImport/"+action)).StatusCode);
        }
        foreach(var action in new[]{"Upload","Map","Select","Prepare","Apply","Refresh","Cancel"})
        {
            Assert.Equal(HttpStatusCode.Unauthorized,(await anon.PostAsync("/AdminPlaceImport/"+action,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await user.PostAsync("/AdminPlaceImport/"+action,Form(""))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync("/AdminPlaceImport/"+action,Form(""))).StatusCode);
        }
        Assert.Contains("Uvoz lokacij",WebUtility.HtmlDecode(await Page(admin,"/AdminPlaceImport")));
    }
    [Fact]
    public async Task PreviewIsReadOnlyOwnedAndFinalFieldsCannotOverrideProtectedState()
    {
        using var admin=Client("admin");using var other=Client("other");
        var index=await Page(admin,"/AdminPlaceImport");await Capture("upload",index);
        var token=Field(index,"__RequestVerificationToken");
        const string csv="Name;Latitude;Longitude;Category;Address;Phone;Website;Description;IsFeatured;ImageUrl;Source\nMr.Pet Test;46;15;UNKNOWN;Glavna 1;+386 123;https://example.org;=FORMULA();true;https://ignored.invalid;PRIVATE-RAW\nMr.Pet Test;46.00001;15;PetShop;;;;;true;;PRIVATE-RAW\nOther Shop;47;15;PetShop;;;;;true;;PRIVATE-RAW\nInvalid;NaN;15;PetShop;;;;;true;;PRIVATE-RAW";
        var id=await Upload(admin,token,csv,filename:"../../<places>.csv");
        var mapping=await Page(admin,$"/AdminPlaceImport/Map/{id}");await Capture("mapping",mapping);
        Assert.Contains("&lt;places&gt;.csv",mapping);Assert.DoesNotContain("PRIVATE-RAW",mapping);
        Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync($"/AdminPlaceImport/Map/{id}")).StatusCode);
        var otherToken=Field(await Page(other,"/AdminPlaceImport"),"__RequestVerificationToken");
        Assert.Equal(HttpStatusCode.NotFound,(await other.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(otherToken,("version","1")))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Empty(await db.Places.ToListAsync());
        var mapped=await admin.PostAsync($"/AdminPlaceImport/Map/{id}",Form(token,("version","0"),("name","0"),("latitude","1"),("longitude","2"),
            ("category","999"),("address","4"),("phone","5"),("website","6"),("description","7"),("sourceId","999")));
        Assert.Equal(HttpStatusCode.Redirect,mapped.StatusCode);
        var preview=await Page(admin,$"/AdminPlaceImport/Preview/{id}");await Capture("preview",preview);
        Assert.DoesNotContain("PRIVATE-RAW",preview);Assert.DoesNotContain("https://ignored.invalid",preview);
        Assert.Contains("POSSIBLE_DUPLICATE",preview);Assert.Contains("INVALID",preview);Assert.Contains("=FORMULA()",preview);
        Assert.Empty(await db.Places.ToListAsync());
        var session=app.Services.GetRequiredService<PlaceImportSessions>().Find(id,"admin")!;
        Assert.Null(session.Csv);Assert.Equal(new[]{2,4},session.Selected.Order());
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminPlaceImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","3")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","2"),("proceed","true")))).StatusCode);
        var confirm=await Page(admin,$"/AdminPlaceImport/Confirm/{id}");await Capture("confirm",confirm);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","2"),
            ("sourceId","999"),("category","DogBeach"),("Name","INJECTED"),("latitude","89"),("longitude","170"),
            ("Phone","INJECTED"),("Website","https://injected.invalid"),("IsFeatured","true"),("rows","4")))).StatusCode);
        var result=await Page(admin,$"/AdminPlaceImport/Result/{id}");await Capture("result",result);
        Assert.Contains("Uvoženih 1 lokacij",WebUtility.HtmlDecode(result));Assert.Contains("Skupaj prebranih vrstic: 4",result);
        Assert.Contains("Neizbrane pripravljene vrstice: 1",result);Assert.Contains("Neveljavno: 1",result);
        var place=Assert.Single(await db.Places.AsNoTracking().ToListAsync());
        Assert.Equal("Mr.Pet Test",place.Name);Assert.Equal(46,place.Latitude);Assert.Equal(15,place.Longitude);
        Assert.Equal(PlaceCategory.PetShop,place.Category);Assert.Equal(1,place.DataSourceId);Assert.Equal("+386 123",place.Phone);
        Assert.Equal("https://example.org",place.WebsiteUrl);Assert.False(place.IsFeatured);Assert.True(place.IsActive);Assert.Null(place.ImageUrl);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","2")))).StatusCode);
        Assert.Equal(1,await db.Places.CountAsync());
    }
    [Fact]
    public async Task MappedCategoriesPublicDiscoveryFriendlyDetailsSeoAndSavedPlacesUseNormalContracts()
    {
        using var admin=Client("admin");using var anon=Client();
        var token=Field(await Page(admin,"/AdminPlaceImport"),"__RequestVerificationToken");
        var id=await Upload(admin,token,"Name;Latitude;Longitude;Category\nČrni Test;46;15;DogPark\nOther;47;15;Unrecognized",category:null);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminPlaceImport/Map/{id}",Form(token,("version","0"),("latitude","1"),("longitude","2")))).StatusCode);
        var response=await admin.PostAsync($"/AdminPlaceImport/Map/{id}",Form(token,("version","0"),("name","0"),("latitude","1"),("longitude","2"),("category","3")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        await admin.PostAsync($"/AdminPlaceImport/Prepare/{id}",Form(token,("version","1")));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var source=await db.DataSources.SingleAsync();source.ContactName="PRIVATE-CONTACT";source.ContactEmail="private@example.invalid";source.Notes="PRIVATE-NOTES";await db.SaveChangesAsync();
        var place=Assert.Single(await db.Places.ToListAsync());Assert.Equal(PlaceCategory.DogPark,place.Category);
        var path=SeoMetadata.PlacePath(place.Id,place.Name);Assert.EndsWith("/crni-test",path);
        foreach(var route in new[]{"/Places",$"/Places/Details/{place.Id}",path})
        {
            var text=await Page(anon,route);Assert.Contains("Test",text);
            foreach(var secret in new[]{"PRIVATE-CONTACT","PRIVATE-NOTES","private@example.invalid","AmenitiesVerifiedAt","ContactEmail"})Assert.DoesNotContain(secret,text);
        }
        var details=await Page(anon,path);Assert.Contains("application/ld+json",details);Assert.Contains(path,details);
        anon.DefaultRequestHeaders.Host="doggydrop.app"; // Loopback transport; synthetic canonical Host only.
        var sitemap=System.Xml.Linq.XDocument.Parse(await Page(anon,"/sitemap.xml"));
        Assert.Contains(sitemap.Descendants().Where(e=>e.Name.LocalName=="loc"),e=>e.Value=="https://doggydrop.app"+path);
        var save=await admin.PostAsync($"/SavedPlaces/Save/{place.Id}",Form(token,("placeId",place.Id.ToString()),("returnUrl",path)));
        Assert.Equal(HttpStatusCode.Redirect,save.StatusCode);
        Assert.True(await db.SavedPlaces.AnyAsync(s=>s.PlaceId==place.Id&&s.UserId=="admin"));
        var again=await Upload(admin,token,"Name;Latitude;Longitude;Category\nČrni Test;46;15;DogPark",category:null);
        await admin.PostAsync($"/AdminPlaceImport/Map/{again}",Form(token,("version","0"),("name","0"),("latitude","1"),("longitude","2"),("category","3")));
        Assert.Empty(app.Services.GetRequiredService<PlaceImportSessions>().Find(again,"admin")!.Selected);
    }
    [Fact]
    public async Task ExpiryMissingSessionAndDeletedSourceDenyWrites()
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminPlaceImport"),"__RequestVerificationToken");
        var id=await Upload(admin,token,"Name;lat;lon\nShop;46;15");await Map(admin,token,id);
        await admin.PostAsync($"/AdminPlaceImport/Prepare/{id}",Form(token,("version","1")));
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.DataSources.ExecuteDeleteAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","1")))).StatusCode);
        Assert.Empty(await db.Places.ToListAsync());
        clock.Now=clock.Now.AddMinutes(31);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync($"/AdminPlaceImport/Preview/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","2")))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.GetAsync("/AdminPlaceImport/Preview/not-in-this-instance")).StatusCode);
    }
    [Fact]
    public async Task PaginationStaleSelectionFinalDuplicateRefreshAndCancelRemainServerAuthoritative()
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminPlaceImport"),"__RequestVerificationToken");
        var csv="Name;lat;lon\n"+string.Join('\n',Enumerable.Range(0,101).Select(i=>$"Shop {i};46;15"));
        var id=await Upload(admin,token,csv);await Map(admin,token,id);
        var session=app.Services.GetRequiredService<PlaceImportSessions>().Find(id,"admin")!;
        Assert.Equal(101,session.Selected.Count);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminPlaceImport/Select/{id}",Form(token,("version","1"),("page","1"),("rows","102")))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Select/{id}",Form(token,("version","1"),("page","1")))).StatusCode);
        Assert.Equal(102,Assert.Single(session.Selected)); // The other page retains its selection.
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.PostAsync($"/AdminPlaceImport/Select/{id}",Form(token,("version","1"),("page","2"),("rows","102")))).StatusCode);
        Assert.Contains("Shop 100",await Page(admin,$"/AdminPlaceImport/Preview/{id}?page=2"));
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Prepare/{id}",Form(token,("version","2")))).StatusCode);
        await using var scope=app.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Places.Add(new Place { Name="Shop 100",Category=PlaceCategory.PetShop,Latitude=46,Longitude=15,IsActive=true });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","2")))).StatusCode);
        Assert.Equal(1,await db.Places.CountAsync());Assert.Null(session.Result);
        Assert.Equal(HttpStatusCode.BadRequest,(await admin.GetAsync($"/AdminPlaceImport/Confirm/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Refresh/{id}",Form(token,("version","3")))).StatusCode);
        Assert.Empty(session.Selected);Assert.Equal(ImportRowStatus.PossibleDuplicate,session.Rows.Last().Status);
        Assert.Equal(HttpStatusCode.Redirect,(await admin.PostAsync($"/AdminPlaceImport/Cancel/{id}",Form(token))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await admin.PostAsync($"/AdminPlaceImport/Apply/{id}",Form(token,("version","4")))).StatusCode);
        Assert.Equal(1,await db.Places.CountAsync());
    }
    [Theory]
    [InlineData("999","PetShop","valid.csv")] [InlineData("","PetShop","valid.csv")]
    [InlineData("1","999","valid.csv")] [InlineData("1","NotCategory","valid.csv")]
    [InlineData("1","PetShop","invalid.xlsx")]
    public async Task UploadRejectsInvalidSourceCategoryOrFormat(string source,string category,string filename)
    {
        using var admin=Client("admin");var token=Field(await Page(admin,"/AdminPlaceImport"),"__RequestVerificationToken");
        using var form=new MultipartFormDataContent();form.Add(new StringContent(token),"__RequestVerificationToken");
        form.Add(new StringContent(source),"sourceId");form.Add(new StringContent(category),"category");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("Name;lat;lon\nShop;46;15")),"file",filename);
        var response=await admin.PostAsync("/AdminPlaceImport/Upload",form);
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);Assert.Contains("validation-summary-errors",await response.Content.ReadAsStringAsync());
    }
    private static async Task Capture(string name,string html)
    {
        var folder=Environment.GetEnvironmentVariable("DOGGYDROP_PLACE_IMPORT_CAPTURE");
        if(!string.IsNullOrEmpty(folder)) { Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,name+".html"),html); }
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
