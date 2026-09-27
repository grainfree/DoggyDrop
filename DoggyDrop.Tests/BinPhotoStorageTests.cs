using System.Net;
using System.Reflection;
using Amazon.S3;
using CloudinaryDotNet;
using DoggyDrop.Controllers;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinPhotoStorageTests : IDisposable
{
    private readonly string root=Path.Combine(Path.GetTempPath(),"doggydrop-bin-storage-"+Guid.NewGuid().ToString("N"));
    [Theory]
    [InlineData("https://res.cloudinary.com/test/image/upload/v12/doggydrop-trashbins/photo_unique.jpg","cloud")]
    [InlineData("/uploads/trashbins/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.png","local")]
    [InlineData("https://r2.test/photos/trashbins/2026/09/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp","r2")]
    [InlineData("https://r2.test/photos/trashbins/optimized/2026/09/123-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp","r2")]
    [InlineData("https://r2.test/photos/trashbins/migrated/2026/09/123-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg","r2")]
    public void OnlyKnownNamespacesAreManaged(string url,string kind)=>Assert.Equal(kind,BinPhotoAssets.Resolve(url,"test","https://r2.test/photos")!.Kind);
    [Theory]
    [InlineData("https://res.cloudinary.com/other/image/upload/v12/doggydrop-trashbins/photo.jpg")]
    [InlineData("https://res.cloudinary.com/test/image/upload/v12/doggydrop-profile-images/photo.jpg")]
    [InlineData("https://res.cloudinary.com/test/image/upload/a_90/v12/doggydrop-trashbins/photo.jpg")]
    [InlineData("https://res.cloudinary.com/test/image/upload/v12/doggydrop-trashbins/photo.svg")]
    [InlineData("https://res.cloudinary.com:444/test/image/upload/v12/doggydrop-trashbins/photo.jpg")]
    [InlineData("https://res.cloudinary.com@test.invalid/test/image/upload/v12/doggydrop-trashbins/photo.jpg")]
    [InlineData("https://res.cloudinary.com/test/image/upload/v12/doggydrop-trashbins/photo.jpg?x=1")]
    [InlineData("https://res.cloudinary.com/test/image/upload/v12/doggydrop-trashbins/%2e%2e/photo.jpg")]
    [InlineData("/uploads/trashbins/../../secret.jpg")]
    [InlineData("/uploads/profile-images/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg")]
    [InlineData("https://r2.test/photos-other/trashbins/2026/09/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.webp")]
    [InlineData("https://external.test/photo.jpg")]
    [InlineData("file:///etc/passwd")]
    public void ForeignTransformedOrAmbiguousTargetsAreNotManaged(string url)=>Assert.Null(BinPhotoAssets.Resolve(url,"test","https://r2.test/photos"));
    private BinPhotoStorage Storage(ICloudinaryService uploads,HttpMessageHandler handler)=>new(uploads,new ImageOptimizationService(),new BinPhotoProcessingTests.TestEnvironment(root),new Clients(handler),new PlaceLogoCloudName("test"),Options.Create(new CloudflareR2Settings()),new Cloudinary(new Account("test","unused","unused")));
    [Fact]
    public async Task ExistingMediaTransferCannotUploadRejectedBinBytes()
    {
        var s3=DispatchProxy.Create<IAmazonS3,BinPhotoProcessingTests.CaptureS3>();
        using var services=new ServiceCollection().AddSingleton(s3).BuildServiceProvider();
        var options=Options.Create(new CloudflareR2Settings{AccountId="test",AccessKeyId="test",SecretAccessKey="test",BucketName="test",PublicBaseUrl="https://r2.test"});
        var controller=new MediaMigrationController(null!,services,options,new Clients(new Download(HttpStatusCode.OK)),new ImageOptimizationService(),NullLogger<MediaMigrationController>.Instance);
        var method=typeof(MediaMigrationController).GetMethod("CopyRemoteImageToR2Async",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var copy=(Task<string>)method.Invoke(controller,[new MediaMigrationItemViewModel{SourceType="Trash bin",EntityId=1,Url=BinPhotoRotationTests.Original},false])!;
        await Assert.ThrowsAsync<InvalidOperationException>(()=>copy);
        Assert.Null(((BinPhotoProcessingTests.CaptureS3)(object)s3).Bytes);
    }
    [Fact]
    public async Task ManagedLocalCopyRotatesAndGetsNewUrlWithoutDeletingSource()
    {
        var uploads=new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,new BinPhotoProcessingTests.TestEnvironment(root),new ImageOptimizationService());
        var original=await uploads.UploadTrashBinImageAsync(BinPhotoProcessingTests.Photo(BinPhotoProcessingTests.ExifPhoto(1)));
        var noNetwork=new Download(HttpStatusCode.NotFound);var store=Storage(uploads,noNetwork);
        Assert.True(store.CanRotate(original));var rotated=await store.RotateCopyAsync(original!,90);Assert.NotNull(rotated);Assert.NotEqual(original,rotated);
        Assert.True(File.Exists(root+original));Assert.True(File.Exists(root+rotated));Assert.Equal(0,noNetwork.Calls);
        using var bitmap=SKBitmap.Decode(File.ReadAllBytes(root+rotated));Assert.Equal(40,bitmap.Width);Assert.Equal(80,bitmap.Height);
        await store.DeleteManagedAsync("https://external.test/photo.jpg");Assert.True(File.Exists(root+original));
        await store.DeleteManagedAsync(original!);Assert.False(File.Exists(root+original));Assert.True(File.Exists(root+rotated));
        Assert.False(store.CanRotate(original));Assert.Null(await store.RotateCopyAsync(original!,90));
    }
    [Theory]
    [InlineData(404,false)] [InlineData(302,false)] [InlineData(200,true)]
    public async Task BrokenRedirectedOrOversizedDownloadsFailWithoutUpload(int status,bool oversized)
    {
        var uploads=new CaptureUpload();var handler=new Download((HttpStatusCode)status){Oversized=oversized};var storage=Storage(uploads,handler);
        Assert.Null(await storage.RotateCopyAsync(BinPhotoRotationTests.Original,90));Assert.Equal(0,uploads.Calls);Assert.Equal(1,handler.Calls);
        Assert.Null(await storage.RotateCopyAsync("https://external.test/image.jpg",90));Assert.Equal(1,handler.Calls);
    }
    [Fact]
    public async Task ManagedCloudDownloadRotatesOriginalBytesBeforeUpload()
    {
        var uploads=new CaptureUpload();var handler=new Download(HttpStatusCode.OK){Bytes=BinPhotoProcessingTests.ExifPhoto(6)};
        var storage=Storage(uploads,handler);var result=await storage.RotateCopyAsync(BinPhotoRotationTests.Original,90);
        Assert.NotNull(result);Assert.Equal(BinPhotoRotationTests.Original,handler.Url);Assert.Equal(1,uploads.Calls);
        using var bitmap=SKBitmap.Decode(uploads.Bytes);Assert.Equal(80,bitmap.Width);Assert.Equal(40,bitmap.Height);
        Assert.DoesNotContain("GPSLatitude",System.Text.Encoding.Latin1.GetString(uploads.Bytes!));
    }
    private sealed class Clients(HttpMessageHandler handler):IHttpClientFactory {public HttpClient CreateClient(string name)=>new(handler,false);}
    private sealed class Download(HttpStatusCode code):HttpMessageHandler
    {
        public int Calls;public bool Oversized;public string? Url;public byte[] Bytes=[255,216,255];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {Calls++;Url=request.RequestUri!.AbsoluteUri;var response=new HttpResponseMessage(code){Content=new ByteArrayContent(Bytes)};if(Oversized)response.Content.Headers.ContentLength=BinPhotoUploadPolicy.MaxBytes+1;return Task.FromResult(response);}
    }
    private sealed class CaptureUpload:ICloudinaryService
    {
        public int Calls;public byte[]? Bytes;
        public Task<string?> UploadImageAsync(IFormFile file)=>throw new NotSupportedException();public Task<string?> UploadWalkImageAsync(IFormFile file)=>throw new NotSupportedException();
        public async Task<string?> UploadTrashBinImageAsync(IFormFile file){Calls++;using var output=new MemoryStream();await file.CopyToAsync(output);Bytes=output.ToArray();return $"https://res.cloudinary.com/test/image/upload/v2/doggydrop-trashbins/{Guid.NewGuid():N}.webp";}
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
