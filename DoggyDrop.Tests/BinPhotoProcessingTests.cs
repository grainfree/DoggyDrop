using System.Net;
using System.Reflection;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using CloudinaryDotNet;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinPhotoProcessingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "doggydrop-bin-photo-" + Guid.NewGuid().ToString("N"));
    private ImageOptimizationService Processor => new();
    internal static IFormFile Photo(byte[] bytes, string name = "photo.jpg", string type = "image/jpeg") =>
        new FormFile(new MemoryStream(bytes),0,bytes.Length,"ImageFile",name) {Headers=new HeaderDictionary(),ContentType=type};

    // Real TIFF orientation IFD in JPEG APP1, plus a private metadata marker.
    internal static byte[] ExifPhoto(ushort orientation)
    {
        using var bitmap=new SKBitmap(80,40);
        using(var canvas=new SKCanvas(bitmap))
        {
            using var paint=new SKPaint();
            paint.Color=SKColors.Red;canvas.DrawRect(0,0,40,20,paint);
            paint.Color=SKColors.Lime;canvas.DrawRect(40,0,40,20,paint);
            paint.Color=SKColors.Blue;canvas.DrawRect(0,20,40,20,paint);
            paint.Color=SKColors.Yellow;canvas.DrawRect(40,20,40,20,paint);
        }
        using var image=SKImage.FromBitmap(bitmap);using var jpeg=image.Encode(SKEncodedImageFormat.Jpeg,100);
        using var exif=new MemoryStream();using(var writer=new BinaryWriter(exif,Encoding.ASCII,true))
        {
            writer.Write("Exif\0\0"u8);writer.Write("II"u8);writer.Write((ushort)42);writer.Write((uint)8);
            writer.Write((ushort)1);writer.Write((ushort)0x112);writer.Write((ushort)3);writer.Write((uint)1);
            writer.Write(orientation);writer.Write((ushort)0);writer.Write((uint)0);writer.Write("GPSLatitude-PRIVATE"u8);
        }
        var metadata=exif.ToArray();var source=jpeg.ToArray();
        return [..source.AsSpan(0,2),255,225,(byte)((metadata.Length+2)>>8),(byte)(metadata.Length+2),..metadata,..source.AsSpan(2)];
    }
    private static void Corners(SKBitmap bitmap, params SKColor[] expected)
    {
        var points=new[]{(bitmap.Width/4,bitmap.Height/4),(bitmap.Width*3/4,bitmap.Height/4),(bitmap.Width/4,bitmap.Height*3/4),(bitmap.Width*3/4,bitmap.Height*3/4)};
        for(var i=0;i<4;i++)
        {
            var actual=bitmap.GetPixel(points[i].Item1,points[i].Item2);
            Assert.InRange(Math.Abs(actual.Red-expected[i].Red),0,35);Assert.InRange(Math.Abs(actual.Green-expected[i].Green),0,35);Assert.InRange(Math.Abs(actual.Blue-expected[i].Blue),0,35);
        }
    }
    [Theory]
    [InlineData(1,80,40)] [InlineData(3,80,40)] [InlineData(6,40,80)] [InlineData(8,40,80)]
    [InlineData(2,80,40)] [InlineData(4,80,40)] [InlineData(5,40,80)] [InlineData(7,40,80)]
    public async Task ExifIsAppliedToPixelsAndMetadataIsRemoved(int origin,int width,int height)
    {
        using var source=new MemoryStream(ExifPhoto((ushort)origin));
        var result=await Processor.OptimizeAsync(source,"image/jpeg","photo.jpg",ImageOptimizationPreset.TrashBin);
        using var content=result.Content;Assert.True(result.WasOptimized);using var output=new MemoryStream();await content.CopyToAsync(output);
        var bytes=output.ToArray();using var bitmap=SKBitmap.Decode(bytes);Assert.Equal(width,bitmap.Width);Assert.Equal(height,bitmap.Height);
        var expected=origin switch {
            2=>new[]{SKColors.Lime,SKColors.Red,SKColors.Yellow,SKColors.Blue},
            3=>new[]{SKColors.Yellow,SKColors.Blue,SKColors.Lime,SKColors.Red},
            4=>new[]{SKColors.Blue,SKColors.Yellow,SKColors.Red,SKColors.Lime},
            5=>new[]{SKColors.Red,SKColors.Blue,SKColors.Lime,SKColors.Yellow},
            6=>new[]{SKColors.Blue,SKColors.Red,SKColors.Yellow,SKColors.Lime},
            7=>new[]{SKColors.Yellow,SKColors.Lime,SKColors.Blue,SKColors.Red},
            8=>new[]{SKColors.Lime,SKColors.Yellow,SKColors.Red,SKColors.Blue},
            _=>new[]{SKColors.Red,SKColors.Lime,SKColors.Blue,SKColors.Yellow}};
        Corners(bitmap,expected);Assert.DoesNotContain("Exif",Encoding.Latin1.GetString(bytes));Assert.DoesNotContain("GPSLatitude",Encoding.Latin1.GetString(bytes));
        using var codec=SKCodec.Create(new MemoryStream(bytes));Assert.Equal(SKEncodedOrigin.TopLeft,codec.EncodedOrigin);
    }
    [Theory]
    [InlineData(90)] [InlineData(180)] [InlineData(270)]
    public async Task ManualRotationChangesPixels(int degrees)
    {
        using var source=new MemoryStream(ExifPhoto(1));var rotated=await Processor.RotateAsync(source,"image/jpeg","photo.jpg",degrees);
        using var content=rotated.Content;using var bitmap=SKBitmap.Decode(content);
        Assert.Equal(degrees==180?80:40,bitmap.Width);
        Corners(bitmap,degrees switch {90=>[SKColors.Blue,SKColors.Red,SKColors.Yellow,SKColors.Lime],180=>[SKColors.Yellow,SKColors.Blue,SKColors.Lime,SKColors.Red],_=>[SKColors.Lime,SKColors.Yellow,SKColors.Red,SKColors.Blue]});
    }
    [Theory]
    [InlineData(ImageOptimizationPreset.Profile)] [InlineData(ImageOptimizationPreset.Walk)] [InlineData(ImageOptimizationPreset.PlaceLogo)]
    public async Task SharedMirroredOrientationTransformAlsoPreservesOtherPresets(ImageOptimizationPreset preset)
    {
        using var input=new MemoryStream(ExifPhoto(7));var result=await Processor.OptimizeAsync(input,"image/jpeg","photo.jpg",preset);
        using var content=result.Content;Assert.True(result.WasOptimized);using var bitmap=SKBitmap.Decode(content);
        Assert.Equal(40,bitmap.Width);Assert.Equal(80,bitmap.Height);Corners(bitmap,SKColors.Yellow,SKColors.Lime,SKColors.Blue,SKColors.Red);
    }
    [Fact]
    public async Task ConsecutiveRotationsUseCurrentPixels()
    {
        byte[] bytes=ExifPhoto(1);
        for(var i=1;i<=4;i++)
        {
            using var input=new MemoryStream(bytes);var result=await Processor.RotateAsync(input,i==1?"image/jpeg":"image/webp",i==1?"a.jpg":"a.webp",90);
            using var content=result.Content;using var output=new MemoryStream();await content.CopyToAsync(output);bytes=output.ToArray();
            using var image=SKBitmap.Decode(bytes);
            if(i==2)Corners(image,SKColors.Yellow,SKColors.Blue,SKColors.Lime,SKColors.Red);
            if(i==4)Corners(image,SKColors.Red,SKColors.Lime,SKColors.Blue,SKColors.Yellow);
        }
    }
    [Theory]
    [InlineData("photo.svg","image/svg+xml")] [InlineData("photo.jpg","image/png")] [InlineData("photo.exe","image/jpeg")]
    public async Task UnsupportedOrMismatchedContentIsRejected(string name,string type)
    {
        using var input=new MemoryStream(ExifPhoto(1));var result=await Processor.OptimizeAsync(input,type,name,ImageOptimizationPreset.TrashBin);
        using(result.Content)Assert.False(result.WasOptimized);
    }
    [Fact]
    public async Task MalformedOversizedAndExtremeDimensionsFailClosed()
    {
        foreach(var bytes in new[]{new byte[]{255,216,255,0},new byte[BinPhotoUploadPolicy.MaxBytes+1]})
        {using var input=new MemoryStream(bytes);var result=await Processor.OptimizeAsync(input,"image/jpeg","a.jpg",ImageOptimizationPreset.TrashBin);using(result.Content)Assert.False(result.WasOptimized);}
        using var huge=new SKBitmap(12001,1);using var image=SKImage.FromBitmap(huge);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        using var stream=new MemoryStream(png.ToArray());var rejected=await Processor.OptimizeAsync(stream,"image/png","a.png",ImageOptimizationPreset.TrashBin);using(rejected.Content)Assert.False(rejected.WasOptimized);
        Assert.False(BinPhotoUploadPolicy.HasSafeDimensions(8000,8000));
    }
    [Fact]
    public async Task BinResizePolicyRemains1200WithoutCropping()
    {
        using var bitmap=new SKBitmap(2400,1200);bitmap.Erase(SKColors.Blue);using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100);
        using var stream=new MemoryStream(png.ToArray());var result=await Processor.OptimizeAsync(stream,"image/png","a.png",ImageOptimizationPreset.TrashBin);
        using var content=result.Content;using var output=SKBitmap.Decode(content);Assert.Equal(1200,output.Width);Assert.Equal(600,output.Height);
    }
    [Fact]
    public async Task LocalUploadsNormalizeAndUseDistinctUrls()
    {
        var service=new MissingCloudinaryService(NullLogger<MissingCloudinaryService>.Instance,new TestEnvironment(root),Processor);
        var first=await service.UploadTrashBinImageAsync(Photo(ExifPhoto(6)));var second=await service.UploadTrashBinImageAsync(Photo(ExifPhoto(6)));
        Assert.NotEqual(first,second);Assert.EndsWith(".webp",first);
        using var bitmap=SKBitmap.Decode(System.IO.File.ReadAllBytes(root+first));Assert.Equal(40,bitmap.Width);Assert.Equal(80,bitmap.Height);
        Assert.Null(await service.UploadTrashBinImageAsync(Photo("<svg/>"u8.ToArray(),"photo.svg","image/svg+xml")));
    }
    [Fact]
    public async Task R2UploadReceivesNormalizedPixelsAndImmutableNewKey()
    {
        var s3=DispatchProxy.Create<IAmazonS3,CaptureS3>();var capture=(CaptureS3)(object)s3;
        var service=new CloudflareR2StorageService(s3,Options.Create(new CloudflareR2Settings {BucketName="test",PublicBaseUrl="https://images.test"}),Processor,NullLogger<CloudflareR2StorageService>.Instance);
        var url=await service.UploadTrashBinImageAsync(Photo(ExifPhoto(8)));
        Assert.EndsWith(".webp",url);Assert.Equal("image/webp",capture.Type);Assert.Contains("immutable",capture.Cache);
        using var bitmap=SKBitmap.Decode(capture.Bytes);Assert.Equal(40,bitmap.Width);Assert.Equal(80,bitmap.Height);
        Assert.Null(await service.UploadTrashBinImageAsync(Photo([255,216,255])));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CloudinaryAndItsLocalFallbackBothReceiveNormalizedPixels(bool fail)
    {
        var handler=new CloudUpload(fail);var cloud=new Cloudinary(new Account("test","unused","unused"));cloud.Api.Client=new HttpClient(handler);
        var service=new CloudinaryService(cloud,new TestEnvironment(root),NullLogger<CloudinaryService>.Instance,Processor);
        var url=await service.UploadTrashBinImageAsync(Photo(ExifPhoto(6)));
        Assert.NotNull(url);using var bitmap=SKBitmap.Decode(handler.Bytes);Assert.Equal(40,bitmap.Width);Assert.Equal(80,bitmap.Height);
        if(fail){using var local=SKBitmap.Decode(System.IO.File.ReadAllBytes(root+url));Assert.Equal(40,local.Width);}
        Assert.DoesNotContain("GPSLatitude",Encoding.Latin1.GetString(handler.Bytes!));
    }
    internal sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string ApplicationName {get;set;}="Tests";public string EnvironmentName {get;set;}="Testing";
        public string ContentRootPath {get;set;}=root;public IFileProvider ContentRootFileProvider {get;set;}=new NullFileProvider();
        public string WebRootPath {get;set;}=root;public IFileProvider WebRootFileProvider {get;set;}=new NullFileProvider();
    }
    public class CaptureS3 : DispatchProxy
    {
        public byte[]? Bytes; public string? Type,Cache;
        protected override object? Invoke(MethodInfo? method,object?[]? args)=> Capture((PutObjectRequest)args![0]!);
        private async Task<PutObjectResponse> Capture(PutObjectRequest request){using var buffer=new MemoryStream();await request.InputStream.CopyToAsync(buffer);Bytes=buffer.ToArray();Type=request.ContentType;Cache=request.Headers.CacheControl;return new();}
    }
    private sealed class CloudUpload(bool fail) : HttpMessageHandler
    {
        public byte[]? Bytes;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
        {
            var multipart=Assert.IsAssignableFrom<MultipartFormDataContent>(request.Content);
            Bytes=await multipart.Single(part=>part.Headers.ContentDisposition?.Name?.Trim('"')=="file").ReadAsByteArrayAsync(ct);
            var id=await multipart.Single(part=>part.Headers.ContentDisposition?.Name?.Trim('"')=="public_id").ReadAsStringAsync(ct);
            return new HttpResponseMessage(fail?HttpStatusCode.BadRequest:HttpStatusCode.OK){Content=new StringContent(fail?"{\"error\":{\"message\":\"test\"}}":System.Text.Json.JsonSerializer.Serialize(new {public_id=id,secure_url=$"https://res.cloudinary.com/test/image/upload/v123/{id}.webp"}),Encoding.UTF8,"application/json")};
        }
    }
    public void Dispose(){if(Directory.Exists(root))Directory.Delete(root,true);}
}
