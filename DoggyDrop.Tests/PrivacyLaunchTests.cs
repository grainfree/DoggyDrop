using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using CloudinaryDotNet;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class ProfilePhotoPrivacyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "profile-privacy-" + Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public async Task LocalProfilePhotosPublishOnlyNormalizedPixels(int orientation)
    {
        var service = new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,
            new BinPhotoProcessingTests.TestEnvironment(root), new ImageOptimizationService());
        var url = await service.UploadImageAsync(BinPhotoProcessingTests.Photo(BinPhotoProcessingTests.ExifPhoto((ushort)orientation)));
        Assert.NotNull(url); Assert.EndsWith(".webp", url);
        var bytes = await File.ReadAllBytesAsync(root + url);
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(bytes));
        Assert.DoesNotContain("GPSLatitude", Encoding.Latin1.GetString(bytes));
        using var bitmap = SKBitmap.Decode(bytes);
        Assert.Equal(orientation >= 5 ? 40 : 80, bitmap.Width);
    }
    [Theory] [InlineData("local")] [InlineData("r2")] [InlineData("cloud")]
    public async Task RejectedProfileOriginalNeverReachesStorage(string backend)
    {
        var s3 = DispatchProxy.Create<IAmazonS3, BinPhotoProcessingTests.CaptureS3>();
        var handler = new CaptureCloud(false);
        var cloud = new Cloudinary(new Account("synthetic", "unused", "unused")); cloud.Api.Client = new HttpClient(handler);
        ICloudinaryService service = backend switch {
            "local" => new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance, new BinPhotoProcessingTests.TestEnvironment(root), new ImageOptimizationService()),
            "r2" => new CloudflareR2StorageService(s3, Options.Create(new CloudflareR2Settings { BucketName="test", PublicBaseUrl="https://media.invalid" }), new ImageOptimizationService(), NullLogger<CloudflareR2StorageService>.Instance),
            _ => new CloudinaryService(cloud, new BinPhotoProcessingTests.TestEnvironment(root), NullLogger<CloudinaryService>.Instance, new ImageOptimizationService()) };
        Assert.Null(await service.UploadImageAsync(BinPhotoProcessingTests.Photo("unreadable-EXIF-GPSLatitude"u8.ToArray(), "image.heic", "image/heic")));
        Assert.Null(await service.UploadImageAsync(BinPhotoProcessingTests.Photo(new byte[WalkPhotoUploadPolicy.MaxBytes + 1])));
        Assert.Null(((BinPhotoProcessingTests.CaptureS3)(object)s3).Bytes); Assert.Null(handler.Bytes);
        Assert.False(Directory.Exists(Path.Combine(root,"uploads")));
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task CloudinaryProfileAndFallbackReceiveSanitizedRandomlyNamedImage(bool fail)
    {
        var handler = new CaptureCloud(fail); var logger = new CaptureLog<CloudinaryService>();
        var cloud = new Cloudinary(new Account("synthetic", "unused", "unused")); cloud.Api.Client = new HttpClient(handler);
        var service = new CloudinaryService(cloud, new BinPhotoProcessingTests.TestEnvironment(root), logger, new ImageOptimizationService());
        var url = await service.UploadImageAsync(BinPhotoProcessingTests.Photo(BinPhotoProcessingTests.ExifPhoto(6), "personal-name.jpg"));
        Assert.NotNull(url); Assert.DoesNotContain("GPSLatitude", Encoding.Latin1.GetString(handler.Bytes!));
        Assert.DoesNotContain("personal-name", handler.FileName); Assert.EndsWith(".webp", handler.FileName);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("PRIVATE_PROVIDER_DETAIL"));
        if (fail) Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(await File.ReadAllBytesAsync(root+url)));
    }
    [Fact] public async Task R2ProfileUploadReceivesOnlySanitizedPixels()
    {
        var s3 = DispatchProxy.Create<IAmazonS3, BinPhotoProcessingTests.CaptureS3>();
        var service = new CloudflareR2StorageService(s3, Options.Create(new CloudflareR2Settings { BucketName="test", PublicBaseUrl="https://media.invalid" }), new ImageOptimizationService(), NullLogger<CloudflareR2StorageService>.Instance);
        Assert.EndsWith(".webp", await service.UploadImageAsync(BinPhotoProcessingTests.Photo(BinPhotoProcessingTests.ExifPhoto(6))));
        Assert.DoesNotContain("GPSLatitude", Encoding.Latin1.GetString(((BinPhotoProcessingTests.CaptureS3)(object)s3).Bytes!));
    }
    private sealed class CaptureCloud(bool fail) : HttpMessageHandler
    {
        public byte[]? Bytes; public string FileName = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var parts = Assert.IsAssignableFrom<MultipartFormDataContent>(request.Content);
            var part = parts.Single(p => p.Headers.ContentDisposition?.Name?.Trim('"') == "file");
            Bytes = await part.ReadAsByteArrayAsync(ct); FileName = (part.Headers.ContentDisposition!.FileName ?? part.Headers.ContentDisposition.FileNameStar ?? "").Trim('"');
            return new(fail ? HttpStatusCode.BadRequest : HttpStatusCode.OK) { Content = new StringContent(fail ? "{\"error\":{\"message\":\"PRIVATE_PROVIDER_DETAIL\"}}" : "{\"secure_url\":\"https://res.cloudinary.com/synthetic/image/upload/v1/doggydrop-profile-images/test.webp\"}", Encoding.UTF8,"application/json") };
        }
    }
    internal sealed class CaptureLog<T> : ILogger<T>
    {
        public List<string> Messages = [];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState,Exception?,string> formatter)
        { Assert.Null(error); Messages.Add(formatter(state,error)); }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root,true); }
}

public sealed partial class PrivacyHttpTests
{
    [Theory]
    [InlineData("/Home/Privacy", "privacy")]
    [InlineData("/Home/Terms", "terms")]
    [InlineData("/Identity/Account/Manage/PersonalData", "personal-data")]
    [InlineData("/Identity/Account/Manage/DeletePersonalData", "delete-data")]
    public async Task PrivacySurfacesRenderWithoutSecretsAndRemainDiscoverable(string path,string scene)
    {
        using var client=Client(path.Contains("Manage") ? "alice" : null);
        var response=await client.GetAsync(path);Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        var html=await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\u00c2",WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("\u0139",WebUtility.HtmlDecode(html));
        Assert.Contains("legal-page",html);Assert.Contains("/Home/Privacy",html);Assert.Contains("/Home/Terms",html);
        Assert.DoesNotContain("Use this page to detail",html);Assert.DoesNotContain("PRIVATE_SUBMITTED_NOTE",html);
        if(scene=="privacy")Assert.Contains("Kontakt za zasebnost trenutno ni na voljo",html);
        var capture=Environment.GetEnvironmentVariable("DOGGYDROP_PRIVACY_CAPTURE");
        if(!string.IsNullOrEmpty(capture)){Directory.CreateDirectory(capture);await File.WriteAllTextAsync(Path.Combine(capture,scene+".html"),html);}
    }
    [Theory]
    [InlineData("admin@doggydrop.app",true)]
    [InlineData("bad@bad",false)]
    [InlineData("<script>alert(1)</script>@example.org",false)]
    [InlineData("a..b@example.org",false)]
    public async Task PrivacyContactUsesOnlyValidatedPublicConfiguration(string configured,bool valid)
    {
        app.Configuration["PublicContact:Email"]=configured;
        using var client=Client();var html=await client.GetStringAsync("/Home/Privacy");
        Assert.Equal(valid,html.Contains("href=\"mailto:admin@doggydrop.app\""));
        if(!valid)Assert.Contains("Kontakt za zasebnost trenutno ni na voljo",html);
        Assert.DoesNotContain("<script>alert(1)",html);
    }
    [Theory] [InlineData("alice")] [InlineData("bob")]
    public async Task ExportIncludesOnlyOwnConfirmationHistoryWithoutInternalOrLocationEvidence(string user)
    {
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var bin=await db.TrashBins.FirstAsync(b=>b.IsApproved);
            foreach(var author in new[]{"alice","bob"}) db.InfrastructureConfirmations.Add(new(){UserId=author,TrashBinId=bin.Id,Type=InfrastructureConfirmationType.TrashBinPresent,CreatedAt=DateTime.UtcNow,EvidenceVersion=Guid.NewGuid()});
            await db.SaveChangesAsync();
        }
        using var client=Client(user);var token=await Token(client,Manage+"PersonalData");
        var response=await client.PostAsync(Manage+"DownloadPersonalData?userId=other",new FormUrlEncodedContent(new Dictionary<string,string>{{"__RequestVerificationToken",token},{"UserId","other"}}));
        Assert.Equal(HttpStatusCode.OK,response.StatusCode);
        using var doc=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var row=Assert.Single(doc.RootElement.GetProperty("InfrastructureConfirmations").EnumerateArray());
        Assert.Equal(new[]{"Id","Type","TrashBinId","WaterPointId","CreatedAt"},row.EnumerateObject().Select(p=>p.Name));
        Assert.Equal(user,doc.RootElement.GetProperty("Account").GetProperty("Id").GetString());
        Assert.True(response.Headers.CacheControl!.NoStore);Assert.True(response.Headers.CacheControl.NoCache);
    }
}


public sealed class ProfileReplacementPrivacyTests
{
    [Theory][InlineData(false,false)][InlineData(false,true)][InlineData(true,false)][InlineData(true,true)]
    public async Task ReplacementOnlyCleansPreviousReferenceAfterSuccessfulSave(bool dog,bool accepted)
    {
        await using var f=await PrivacyTestDatabase.Create();await PrivacyLifecycleTests.Seed(f.Db);
        var user=await f.Db.Users.SingleAsync(u=>u.Id=="alice");
        var owned=await f.Db.Dogs.SingleAsync(d=>d.OwnerId=="alice");
        var old=dog?owned.PhotoUrl:user.ProfileImageUrl;
        var cleanup=new SavedCleanup(async()=>{
            await using var committed=new ApplicationDbContext(f.Options);
            Assert.Equal("/uploads/profile-images/new.webp",dog?(await committed.Dogs.FindAsync(owned.Id))!.PhotoUrl:(await committed.Users.FindAsync("alice"))!.ProfileImageUrl);
        });
        var context=new Microsoft.AspNetCore.Http.DefaultHttpContext{User=new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(new[]{new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier,"alice")},"test"))};
        var storage=new SyntheticStorage(accepted);
        if(dog){
            var c=new DoggyDrop.Controllers.DogsController(f.Db,f.Users,storage,null!,null!,cleanup);
            c.ControllerContext=new(){HttpContext=context};c.TempData=new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(context,new EmptyTempData());
            await c.UpdatePhoto(owned.Id,BinPhotoProcessingTests.Photo([1]));
        }else{
            var c=new DoggyDrop.Controllers.HomeController(NullLogger<DoggyDrop.Controllers.HomeController>.Instance,f.Users,storage,null!,f.Db,null!,null!,null!,null!,null!,cleanup);
            c.ControllerContext=new(){HttpContext=context};await c.UpdateProfile("Synthetic",BinPhotoProcessingTests.Photo([1]));
        }
        Assert.Equal(accepted?new[]{old}:Array.Empty<string?>(),cleanup.Urls);
    }
    private sealed class SavedCleanup(Func<Task> assertSaved):IUserMediaCleanup{
        public List<string?> Urls=[];
        public async Task CleanupAsync(IEnumerable<string?> urls){await assertSaved();Urls.AddRange(urls);}
    }
    private sealed class SyntheticStorage(bool accepted):ICloudinaryService{
        public Task<string?> UploadImageAsync(Microsoft.AspNetCore.Http.IFormFile file)=>Task.FromResult(accepted?"/uploads/profile-images/new.webp":null);
        public Task<string?> UploadTrashBinImageAsync(Microsoft.AspNetCore.Http.IFormFile file)=>throw new InvalidOperationException();
        public Task<string?> UploadWalkImageAsync(Microsoft.AspNetCore.Http.IFormFile file)=>throw new InvalidOperationException();
    }
    private sealed class EmptyTempData:Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider{
        public IDictionary<string,object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context)=>new Dictionary<string,object>();
        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context,IDictionary<string,object> values){}
    }
}
