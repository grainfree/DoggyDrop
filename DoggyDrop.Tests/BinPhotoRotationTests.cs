using System.Data.Common;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class BinPhotoRotationTests : IDisposable
{
    private readonly string database=Path.Combine(Path.GetTempPath(),"doggydrop-bin-rotation-"+Guid.NewGuid().ToString("N")+".db");
    internal const string Original="https://res.cloudinary.com/test/image/upload/v1/doggydrop-trashbins/original.jpg";
    private DbContextOptions<ApplicationDbContext> Options(DbCommandInterceptor? interceptor=null)
    {
        var builder=new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite($"Data Source={database};Pooling=False");
        if(interceptor!=null)builder.AddInterceptors(interceptor);return builder.Options;
    }
    private async Task Seed(string? image=Original)
    {
        await using var db=new ApplicationDbContext(Options());await db.Database.EnsureCreatedAsync();
        db.TrashBins.Add(new TrashBin{Name="Bin",Latitude=46,Longitude=15,ImageUrl=image,IsApproved=true,UsedCount=7,ApprovedAt=DateTime.UtcNow});await db.SaveChangesAsync();
    }
    private BinPhotoRotationService Service(ApplicationDbContext db,FakeBinPhotoStorage store,IBinPhotoReferences? references=null)=>
        new(db,store,references??new BinPhotoReferences(Options()),NullLogger<BinPhotoRotationService>.Instance);
    [Theory]
    [InlineData("left",270)] [InlineData("right",90)] [InlineData("half",180)]
    public async Task RotationChangesOnlyImageAndCleansOldAfterSuccess(string operation,int degrees)
    {
        await Seed();await using var db=new ApplicationDbContext(Options());var store=new FakeBinPhotoStorage();
        Assert.True((await Service(db,store).RotateAsync(1,operation)).Success);
        var bin=await db.TrashBins.AsNoTracking().SingleAsync();Assert.Equal(store.Created.Single(),bin.ImageUrl);Assert.NotEqual(Original,bin.ImageUrl);
        Assert.Equal(degrees,store.Operations.Single().Degrees);Assert.Equal(Original,store.Operations.Single().Url);Assert.Contains(Original,store.Deleted);
        Assert.Equal(46,bin.Latitude);Assert.Equal(15,bin.Longitude);Assert.True(bin.IsApproved);Assert.Equal(7,bin.UsedCount);
        Assert.Empty(await db.UserNotifications.ToListAsync());Assert.Empty(await db.UserXpEvents.ToListAsync());
    }
    [Theory]
    [InlineData(null)] [InlineData("https://elsewhere.test/bin.jpg")]
    public async Task MissingOrUnmanagedImageIsNotProcessed(string? image)
    {
        await Seed(image);await using var db=new ApplicationDbContext(Options());var store=new FakeBinPhotoStorage();
        Assert.False((await Service(db,store).RotateAsync(1,"right")).Success);Assert.Empty(store.Created);
        Assert.True((await Service(db,store).RotateAsync(999,"right")).Missing);
        Assert.False((await Service(db,store).RotateAsync(1,"arbitrary")).Success);Assert.Empty(store.Operations);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UploadFailurePreservesOldReference(bool throws)
    {
        await Seed();await using var db=new ApplicationDbContext(Options());var store=new FakeBinPhotoStorage{FailUpload=true,ThrowUpload=throws};
        Assert.False((await Service(db,store).RotateAsync(1,"right")).Success);
        Assert.Equal(Original,(await db.TrashBins.SingleAsync()).ImageUrl);Assert.Empty(store.Deleted);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task UnknownDatabaseOutcomeRetainsBothAssets(bool afterExecution)
    {
        await Seed();await using var db=new ApplicationDbContext(Options(new FailUpdate(afterExecution)));var store=new FakeBinPhotoStorage();
        var result=await Service(db,store).RotateAsync(1,"right");Assert.False(result.Success);Assert.Empty(store.Deleted);
        await using var fresh=new ApplicationDbContext(Options());Assert.Equal(afterExecution?store.Created.Single():Original,(await fresh.TrashBins.SingleAsync()).ImageUrl);
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CleanupOrReferenceReadFailureCannotLoseNewPhoto(bool readFailure)
    {
        await Seed();await using var db=new ApplicationDbContext(Options());var store=new FakeBinPhotoStorage{FailDelete=!readFailure};
        Assert.True((await Service(db,store,readFailure?new FailedReferences():null).RotateAsync(1,"right")).Success);
        Assert.Equal(store.Created.Single(),(await db.TrashBins.SingleAsync()).ImageUrl);Assert.Empty(store.Deleted);
    }
    [Fact]
    public async Task ConcurrentRequestsUseCompareAndSwapAndCleanOnlyLosingCopy()
    {
        await Seed();await using var first=new ApplicationDbContext(Options());await using var second=new ApplicationDbContext(Options());
        var reached1=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var reached2=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release1=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release2=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var a=new FakeBinPhotoStorage{Wait=async()=>{reached1.SetResult();await release1.Task.WaitAsync(TimeSpan.FromSeconds(10));}};
        var b=new FakeBinPhotoStorage{Wait=async()=>{reached2.SetResult();await release2.Task.WaitAsync(TimeSpan.FromSeconds(10));}};
        var pendingA=Service(first,a).RotateAsync(1,"right");await reached1.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var pendingB=Service(second,b).RotateAsync(1,"left");await reached2.Task.WaitAsync(TimeSpan.FromSeconds(10));
        release1.SetResult();Assert.True((await pendingA).Success);release2.SetResult();Assert.False((await pendingB).Success);
        await using var read=new ApplicationDbContext(Options());Assert.Equal(a.Created.Single(),(await read.TrashBins.SingleAsync()).ImageUrl);
        Assert.Contains(b.Created.Single(),b.Deleted);Assert.DoesNotContain(a.Created.Single(),b.Deleted);
        a.Wait=null;
        Assert.True((await Service(read,a).RotateAsync(1,"right")).Success);Assert.Equal(a.Created[0],a.Operations[1].Url);
    }
    [Fact]
    public async Task SharedDeliveryAliasPreventsDeletingReferencedAsset()
    {
        await Seed();await using var db=new ApplicationDbContext(Options());
        db.TrashBins.Add(new TrashBin{Name="Shared",ImageUrl=Original.Replace("/upload/","/upload/f_auto,q_auto/").Replace(".jpg",".webp")});await db.SaveChangesAsync();
        var store=new FakeBinPhotoStorage();Assert.True((await Service(db,store).RotateAsync(1,"right")).Success);Assert.Empty(store.Deleted);
    }
    private sealed class FailedReferences : IBinPhotoReferences {public Task<bool> IsReferencedAsync(string url)=>throw new IOException("test");}
    private sealed class FailUpdate(bool after) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<int> r,CancellationToken ct=default)
        {if(!after&&c.CommandText.StartsWith("UPDATE"))throw new IOException("before");return ValueTask.FromResult(r);}
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand c,CommandExecutedEventData e,int result,CancellationToken ct=default)
        {if(after&&c.CommandText.StartsWith("UPDATE"))throw new IOException("after committed");return ValueTask.FromResult(result);}
    }
    public void Dispose(){if(File.Exists(database))File.Delete(database);}
}

internal sealed class FakeBinPhotoStorage : IBinPhotoStorage
{
    public List<string> Created {get;}=[];public List<string> Deleted {get;}=[];public List<(string Url,int Degrees)> Operations {get;}=[];
    public bool FailUpload,ThrowUpload,FailDelete;public Func<Task>? Wait;
    public bool CanRotate(string? url)=>BinPhotoAssets.Resolve(url,"test",null)!=null;
    public async Task<string?> RotateCopyAsync(string url,int degrees)
    {
        Operations.Add((url,degrees));if(ThrowUpload)throw new IOException("upload");if(FailUpload)return null;
        var replacement=$"https://res.cloudinary.com/test/image/upload/v2/doggydrop-trashbins/{Guid.NewGuid():N}.webp";Created.Add(replacement);
        if(Wait!=null)await Wait();return replacement;
    }
    public Task DeleteManagedAsync(string url){if(FailDelete)throw new IOException("cleanup");Deleted.Add(url);return Task.CompletedTask;}
}
