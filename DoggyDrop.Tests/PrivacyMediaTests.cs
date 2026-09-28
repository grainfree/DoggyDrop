using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PrivacyMediaTests
{
    private const string CloudUrl = "https://res.cloudinary.com/ours/image/upload/v123/doggydrop-walks/photo_abc.jpg";
    private const string R2Base = "https://media.example.invalid";
    [Theory]
    [InlineData("https://evil.invalid/image.jpg")]
    [InlineData("https://res.cloudinary.com/theirs/image/upload/v123/doggydrop-walks/photo_abc.jpg")]
    [InlineData("https://res.cloudinary.com/ours/image/upload/v123/unrelated/photo_abc.jpg")]
    [InlineData("https://res.cloudinary.com/ours/image/upload/v123/doggydrop-walks/photo_abc.jpg?secret=1")]
    [InlineData("https://res.cloudinary.com/ours/image/upload/v123/doggydrop-walks/photo_abc.jpg#fragment")]
    [InlineData("https://res.cloudinary.com.evil.invalid/ours/image/upload/v123/doggydrop-walks/photo_abc.jpg")]
    [InlineData("https://user@res.cloudinary.com/ours/image/upload/v123/doggydrop-walks/photo_abc.jpg")]
    [InlineData("https://res.cloudinary.com:444/ours/image/upload/v123/doggydrop-walks/photo_abc.jpg")]
    [InlineData("/uploads/walks/../profile-images/00000000000000000000000000000000.webp")]
    [InlineData("/uploads/walks/%2e%2e/file.webp")]
    [InlineData("/uploads/walks/00000000000000000000000000000000.webp?x=1")]
    [InlineData("/uploads/walks/arbitrary.webp")]
    [InlineData("https://media.example.invalid/unrelated/2026/09/00000000000000000000000000000000.webp")]
    public void UnmanagedUrlsNeverResolve(string url) => Assert.Null(UserMediaAssets.Resolve(url,"ours",R2Base));

    [Theory]
    [InlineData(CloudUrl,"cloud","doggydrop-walks/photo_abc")]
    [InlineData("https://res.cloudinary.com/ours/image/upload/v123/doggydrop-walks/my.dog_abc.jpg","cloud","doggydrop-walks/my.dog_abc")]
    [InlineData("https://res.cloudinary.com/ours/image/upload/v123/doggydrop-profile-images/photo_abc.png","cloud","doggydrop-profile-images/photo_abc")]
    [InlineData("https://media.example.invalid/walks/2026/09/00000000000000000000000000000000.webp","r2","walks/2026/09/00000000000000000000000000000000.webp")]
    [InlineData("https://media.example.invalid/dogs/migrated/2026/09/42-00000000000000000000000000000000.jpg","r2","dogs/migrated/2026/09/42-00000000000000000000000000000000.jpg")]
    [InlineData("/uploads/profile-images/00000000000000000000000000000000.heic","local","profile-images/00000000000000000000000000000000.heic")]
    public void KnownUploadNamespacesResolve(string url,string kind,string key) => Assert.Equal(new UserMediaAsset(kind,key),UserMediaAssets.Resolve(url,"ours",R2Base));

    [Fact] public Task ReferenceChecksAndFailureSafety() => CheckCleanup();
    internal static async Task CheckCleanup(DbContextOptions<ApplicationDbContext>? options = null)
    {
        await using var fixture=await PrivacyTestDatabase.Create(options);
        var storage=new FakeStorage(); var logger=new SafeLogger();
        var cleanup=new UserMediaCleanup(fixture.Options,storage,logger);
        fixture.Db.Places.Add(new Place { Name="Shared", ImageUrl="https://res.cloudinary.com/ours/image/upload/c_fill,w_100/v124/doggydrop-walks/photo_abc.webp?cache=1" });
        await fixture.Db.SaveChangesAsync();
        await cleanup.CleanupAsync([CloudUrl,CloudUrl,"https://evil.invalid/private-route-secret.jpg"]);
        Assert.Empty(storage.Deleted);
        await fixture.Db.Places.ExecuteDeleteAsync();
        await cleanup.CleanupAsync([CloudUrl,CloudUrl]);
        Assert.Single(storage.Deleted);
        storage.Fail=true;
        await cleanup.CleanupAsync([CloudUrl]); // best effort, no exception detail/URL in logs
        Assert.Single(logger.Messages); Assert.Contains("cloud",logger.Messages.Single());
        Assert.DoesNotContain("private-route-secret",logger.Messages.Single());
        Assert.DoesNotContain("photo_abc",logger.Messages.Single());
        Assert.Equal(0,await fixture.Db.Places.CountAsync());
    }

    [Fact]
    public async Task AccountStorageFailureDoesNotPreventCommittedDeletion()
    {
        await using var fixture=await PrivacyTestDatabase.Create();
        fixture.Db.Users.Add(new ApplicationUser { Id="alice", UserName="alice", ProfileImageUrl=CloudUrl });
        await fixture.Db.SaveChangesAsync();
        var storage=new FakeStorage { Fail=true };
        var cleanup=new UserMediaCleanup(fixture.Options,storage,new SafeLogger());
        var result=await new AccountDataDeletion(fixture.Db,fixture.Users,cleanup).DeleteAsync(await fixture.Db.Users.SingleAsync());
        Assert.True(result.Succeeded); Assert.Empty(await fixture.Db.Users.ToListAsync()); Assert.Single(storage.Deleted);
    }

    [Fact]
    public async Task LocalCleanupOnlyDeletesAllowlistedFileAndIsIdempotent()
    {
        var root=Path.Combine(Path.GetTempPath(),"privacy-media-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root,"uploads","walks"));
        var managed=Path.Combine(root,"uploads","walks","00000000000000000000000000000000.webp");
        var unrelated=Path.Combine(root,"keep.txt");
        await File.WriteAllTextAsync(managed,"synthetic"); await File.WriteAllTextAsync(unrelated,"preserve");
        try
        {
            var storage=new UserMediaStorage(new EnvironmentStub(root),Options.Create(new CloudflareR2Settings()));
            var asset=storage.Resolve("/uploads/walks/00000000000000000000000000000000.webp")!;
            await storage.DeleteAsync(new UserMediaAsset("local","../../keep.txt"));
            Assert.True(File.Exists(unrelated));
            await storage.DeleteAsync(asset); await storage.DeleteAsync(asset);
            Assert.False(File.Exists(managed)); Assert.True(File.Exists(unrelated));
        }
        finally { File.Delete(unrelated); Directory.Delete(Path.Combine(root,"uploads","walks")); Directory.Delete(Path.Combine(root,"uploads")); Directory.Delete(root); }
    }

    private sealed class FakeStorage : IUserMediaStorage
    {
        public bool Fail;
        public List<UserMediaAsset> Deleted=[];
        public UserMediaAsset? Resolve(string? url)=>UserMediaAssets.Resolve(url,"ours",R2Base);
        public bool References(UserMediaAsset asset,string? url)=>UserMediaAssets.References(asset,url,"ours",R2Base);
        public Task DeleteAsync(UserMediaAsset asset)
        {
            Deleted.Add(asset);
            if(Fail) throw new IOException("private-route-secret; provider-url-and-credential");
            return Task.CompletedTask;
        }
    }
    private sealed class SafeLogger : ILogger<UserMediaCleanup>
    {
        public List<string> Messages=[];
        public IDisposable? BeginScope<TState>(TState state) where TState:notnull => null;
        public bool IsEnabled(LogLevel level)=>true;
        public void Log<TState>(LogLevel level,EventId id,TState state,Exception? error,Func<TState,Exception?,string> formatter)
        { Assert.Null(error); Messages.Add(formatter(state,error)); }
    }
    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; }=root;
        public string ContentRootPath { get; set; }=root;
        public string ApplicationName { get; set; }="PrivacyTest";
        public string EnvironmentName { get; set; }="Testing";
        public IFileProvider WebRootFileProvider { get; set; }=new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; }=new NullFileProvider();
    }
}
