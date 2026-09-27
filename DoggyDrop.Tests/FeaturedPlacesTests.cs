using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Migrations;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class FeaturedPlacesTests : IDisposable
{
    internal static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private readonly string file = Path.Combine(Path.GetTempPath(), $"featured-{Guid.NewGuid():N}.db");
    private readonly Reads reads = new();
    private DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={file};Pooling=False").AddInterceptors(reads).Options;
    private async Task<ApplicationDbContext> Context()
    { var db = new ApplicationDbContext(Options()); await db.Database.EnsureCreatedAsync(); return db; }
    private static PlaceInput Input() => new() { Name = "Place", Category = PlaceCategory.Veterinarian, Latitude = 46, Longitude = 15 };
    private AdminPlacesController Admin(ApplicationDbContext db)
    {
        var controller = new AdminPlacesController(db, new MissingPlaceLogoStorage(), new PlaceLogoReferenceReader(Options()),
            new PlaceLogoCloudName("test"), NullLogger<AdminPlacesController>.Instance);
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext() };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new MemoryTempData());
        return controller;
    }
    private PlacesController Public(ApplicationDbContext db, TimeProvider? clock = null) => new(db, new PlaceLogoCloudName("test"), clock ?? new Clock())
    { ControllerContext = new() { HttpContext = new DefaultHttpContext() } };
    internal sealed class Clock : TimeProvider
    { public DateTime Value = Now; public override DateTimeOffset GetUtcNow() => new(Value); }

    [Theory]
    [InlineData(false,true,1,null,null,false)]
    [InlineData(true,true,1,null,null,true)]
    [InlineData(true,true,2,0,null,true)]
    [InlineData(true,true,3,1,null,false)]
    [InlineData(true,true,4,null,0,false)]
    [InlineData(true,true,5,null,1,true)]
    [InlineData(true,true,1,-1,1,true)]
    [InlineData(true,true,1,null,-1,false)]
    [InlineData(true,false,1,null,null,false)]
    [InlineData(true,true,6,null,null,false)]
    [InlineData(true,true,7,null,null,false)]
    [InlineData(true,true,99,null,null,false)]
    public async Task CentralRuleHasIdenticalSqlAndInMemoryBoundaries(bool enabled,bool active,int category,int? from,int? until,bool expected)
    {
        await using var db = await Context();
        var place = new Place {Name="Boundary",Category=(PlaceCategory)category,IsFeatured=enabled,IsActive=active,
            FeaturedFrom=from.HasValue?Now.AddSeconds(from.Value):null,FeaturedUntil=until.HasValue?Now.AddSeconds(until.Value):null};
        db.Places.Add(place);await db.SaveChangesAsync();
        Assert.Equal(expected,FeaturedPlaces.IsCurrent(place,Now));
        Assert.Equal(expected,await db.Places.WithFeatured(Now).Select(p=>p.IsCurrentlyFeatured).SingleAsync());
    }
    [Theory]
    [InlineData("2026-01-15T12:30", "2026-01-15T11:30:00Z")]
    [InlineData("2026-07-15T12:30", "2026-07-15T10:30:00Z")]
    [InlineData("2026-03-29T01:59", "2026-03-29T00:59:00Z")]
    [InlineData("2026-03-29T03:00", "2026-03-29T01:00:00Z")]
    [InlineData("2026-10-25T01:59", "2026-10-24T23:59:00Z")]
    [InlineData("2026-10-25T03:00", "2026-10-25T02:00:00Z")]
    public void LocalInputConvertsToUtcAcrossDst(string local,string expected)
    {
        Assert.True(FeaturedLocalTime.TryUtc(local,out var utc,out var error));Assert.Null(error);
        Assert.Equal(DateTime.Parse(expected,null,System.Globalization.DateTimeStyles.AdjustToUniversal),utc);
        Assert.Equal(DateTimeKind.Utc,utc!.Value.Kind);
        Assert.StartsWith(local,FeaturedLocalTime.ForInput(utc));
    }
    [Theory]
    [InlineData("2026-03-29T02:30")]
    [InlineData("2026-10-25T02:30")]
    [InlineData("2026-09-27T12:00Z")]
    [InlineData("not a date")]
    public void InvalidOrAmbiguousLocalTimesAreRejected(string local)
    {
        Assert.False(FeaturedLocalTime.TryUtc(local,out var utc,out var error));Assert.Null(utc);Assert.NotEmpty(error!);
        var input=Input();input.FeaturedFromLocal=local;
        Assert.Contains(input.Validate(),e=>e.Field==nameof(input.FeaturedFromLocal));
    }
    [Theory]
    [InlineData("2026-09-27T14:00","2026-09-27T14:00",false)]
    [InlineData("2026-09-27T15:00","2026-09-27T14:00",false)]
    [InlineData("2026-09-27T13:00","2026-09-27T14:00",true)]
    [InlineData(null,null,true)]
    [InlineData(null,"2026-09-27T14:00",true)]
    [InlineData("2026-09-27T14:00",null,true)]
    public async Task AdminValidatesRangesOnServer(string? from,string? until,bool valid)
    {
        await using var db=await Context();var input=Input();input.IsFeatured=true;input.FeaturedFromLocal=from;input.FeaturedUntilLocal=until;
        var result=await Admin(db).Create(input);
        if(valid) { Assert.IsType<RedirectToActionResult>(result);Assert.True((await db.Places.SingleAsync()).IsFeatured); }
        else { Assert.IsType<ViewResult>(result);Assert.Empty(await db.Places.ToListAsync()); }
    }
    [Theory]
    [InlineData(PlaceCategory.DogPark)] [InlineData(PlaceCategory.DogBeach)]
    public async Task IneligibleEnableIsRejectedAndCategoryChangeClearsSchedule(PlaceCategory category)
    {
        await using var db=await Context();var create=Input();create.Category=category;create.IsFeatured=true;
        Assert.IsType<ViewResult>(await Admin(db).Create(create));Assert.Empty(await db.Places.ToListAsync());
        create.Category=PlaceCategory.Groomer;create.FeaturedFromLocal="2026-09-27T13:00";
        Assert.IsType<RedirectToActionResult>(await Admin(db).Create(create));
        var place=await db.Places.SingleAsync();var edit=PlaceInput.FromPlace(place,"test");edit.Category=category;edit.IsFeatured=false;
        Assert.IsType<RedirectToActionResult>(await Admin(db).Edit(place.Id,edit));
        Assert.False(place.IsFeatured);Assert.Null(place.FeaturedFrom);Assert.Null(place.FeaturedUntil);
    }
    [Fact]
    public async Task EnableDisableScheduleAndInactivePreserveIndependentFacts()
    {
        await using var db=await Context();db.DataSources.Add(new DataSource{Name="Partner",Type=DataSourceType.Partner});await db.SaveChangesAsync();
        var input=Input();input.DataSourceId=1;input.AmenityTypes=[PlaceAmenityType.WaterForDogs];input.VerifyAmenitiesToday=true;
        input.AmenitiesSourceUrl="https://example.com/facts";await Admin(db).Create(input);
        var place=await db.Places.Include(p=>p.Amenities).SingleAsync();Assert.False(place.IsFeatured);Assert.Null(place.FeaturedFrom);Assert.Null(place.FeaturedUntil);
        var verification=place.AmenitiesVerifiedAt;
        foreach(var enabled in new[]{true,false,true})
        {
            var edit=PlaceInput.FromPlace(place,"test");edit.IsFeatured=enabled;edit.FeaturedFromLocal="2026-09-28T14:00";
            Assert.IsType<RedirectToActionResult>(await Admin(db).Edit(place.Id,edit));
            Assert.Equal(enabled,place.IsFeatured);Assert.Equal(new DateTime(2026,9,28,12,0,0,DateTimeKind.Utc),place.FeaturedFrom);
            Assert.Equal(verification,place.AmenitiesVerifiedAt);Assert.Equal(input.AmenitiesSourceUrl,place.AmenitiesSourceUrl);
            Assert.Equal(1,place.DataSourceId);Assert.Single(place.Amenities);
        }
        await Admin(db).SetActive(place.Id,false);Assert.True(place.IsFeatured);Assert.NotNull(place.FeaturedFrom);
        Assert.False(FeaturedPlaces.IsCurrent(place,Now.AddDays(5)));
    }
    [Fact]
    public async Task OverlappingFeaturedEditsConflictAndRetainAmenityVerification()
    {
        await using var seed=await Context();var input=Input();input.AmenityTypes=[PlaceAmenityType.WaterForDogs];input.VerifyAmenitiesToday=true;
        await Admin(seed).Create(input);var original=await seed.Places.Include(p=>p.Amenities).SingleAsync();
        await seed.Entry(original).ReloadAsync(); // Compare persisted precision on PostgreSQL too.
        var pause=new Pause();await using var delayedDb=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(Options()).AddInterceptors(pause).Options);
        var delayed=Admin(delayedDb);var old=PlaceInput.FromPlace(original,"test");old.IsFeatured=true;old.FeaturedUntilLocal="2026-10-01T12:00";
        var pending=delayed.Edit(original.Id,old);
        try {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await using var winner=new ApplicationDbContext(Options());var current=PlaceInput.FromPlace(original,"test");current.IsFeatured=true;current.FeaturedUntilLocal="2026-11-01T12:00";
            Assert.IsType<RedirectToActionResult>(await Admin(winner).Edit(original.Id,current));
        } finally {pause.Release.TrySetResult();}
        Assert.IsType<ViewResult>(await pending);Assert.False(delayed.ModelState.IsValid);
        await using var read=new ApplicationDbContext(Options());var saved=await read.Places.Include(p=>p.Amenities).SingleAsync();
        Assert.Equal(new DateTime(2026,11,1,11,0,0,DateTimeKind.Utc),saved.FeaturedUntil);
        Assert.Equal(original.AmenitiesVerifiedAt,saved.AmenitiesVerifiedAt);Assert.Single(saved.Amenities);
        AssertOriginalUpdatePredicate(reads.Commands.Last(c => c.Sql.Contains("UPDATE \"Places\"")), original.UpdatedAt);
    }

    [Fact]
    public async Task OriginalFormVersionPreservesMicrosecondsAndRejectsSequentialStaleEdit()
    {
        await using var seed = await Context();
        var timestamp = new DateTime(2099, 1, 1, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234560);
        var place = new Place { Name = "Precision", Category = PlaceCategory.PetShop, UpdatedAt = timestamp };
        seed.Places.Add(place); await seed.SaveChangesAsync();
        await using var firstDb = new ApplicationDbContext(Options());
        await using var secondDb = new ApplicationDbContext(Options());
        var a = Assert.IsType<PlaceInput>(Assert.IsType<ViewResult>(await Admin(firstDb).Edit(place.Id)).Model);
        var b = Assert.IsType<PlaceInput>(Assert.IsType<ViewResult>(await Admin(secondDb).Edit(place.Id)).Model);
        Assert.Equal("2099-01-01T12:34:56.1234560Z", a.OriginalUpdatedAt);
        Assert.True(a.TryOriginalUpdatedAt(out var parsed)); Assert.Equal(timestamp, parsed);
        a.Name = "Winner"; a.IsFeatured = true; a.AmenityTypes = [PlaceAmenityType.WaterForDogs]; a.VerifyAmenitiesToday = true;
        Assert.IsType<RedirectToActionResult>(await Admin(firstDb).Edit(place.Id, a));
        AssertOriginalUpdatePredicate(reads.Commands.Last(c => c.Sql.Contains("UPDATE \"Places\"")), timestamp);
        b.Name = "Stale"; b.Category = PlaceCategory.DogPark; b.AmenityTypes = [PlaceAmenityType.Fenced];
        var loser = Admin(secondDb);
        Assert.IsType<ViewResult>(await loser.Edit(place.Id, b)); Assert.False(loser.ModelState.IsValid);
        await using var read = new ApplicationDbContext(Options());
        var saved = await read.Places.Include(p => p.Amenities).SingleAsync();
        Assert.Equal("Winner", saved.Name); Assert.True(saved.IsFeatured); Assert.Equal(PlaceCategory.PetShop, saved.Category);
        Assert.Equal(PlaceAmenityType.WaterForDogs, Assert.Single(saved.Amenities).AmenityType);
        Assert.NotNull(saved.AmenitiesVerifiedAt); Assert.True(saved.UpdatedAt > timestamp);
    }

    private static void AssertOriginalUpdatePredicate((string Sql, Dictionary<string, object?> Parameters) command, DateTime original)
    {
        var predicate = System.Text.RegularExpressions.Regex.Match(command.Sql,
            "WHERE \\\"Id\\\" = (@p[0-9]+) AND \\\"UpdatedAt\\\" = (@p[0-9]+)");
        Assert.True(predicate.Success, command.Sql);
        var expected = Assert.IsType<DateTime>(command.Parameters[predicate.Groups[2].Value]);
        Assert.Equal(original.Ticks, expected.Ticks);
    }
    [Fact]
    public async Task PublicQueriesUseClockPromoteOnlyCurrentAndDoNotLeakSchedulesOrAddQueries()
    {
        await using var db=await Context();
        db.Places.AddRange(new Place{Name="A normal",Category=PlaceCategory.Veterinarian},
            new Place{Name="Z current",Category=PlaceCategory.PetShop,IsFeatured=true,FeaturedUntil=Now.AddSeconds(1)},
            new Place{Name="B expired",Category=PlaceCategory.PetShop,IsFeatured=true,FeaturedUntil=Now},
            new Place{Name="C future",Category=PlaceCategory.PetShop,IsFeatured=true,FeaturedFrom=Now.AddSeconds(1)},
            new Place{Name="D park",Category=PlaceCategory.DogPark,IsFeatured=true},
            new Place{Name="Hidden",Category=PlaceCategory.PetShop,IsFeatured=true,IsActive=false});
        await db.SaveChangesAsync();reads.Sql.Clear();
        var clock=new Clock();var controller=Public(db,clock);
        var model=Assert.IsType<PlaceDiscoveryViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        Assert.Equal(new[]{"Z current","A normal","B expired","C future","D park"},model.Places.Select(p=>p.Name));
        Assert.Single(model.Places,p=>p.IsCurrentlyFeatured);Assert.Single(reads.Sql);
        Assert.DoesNotContain("AmenitiesSourceUrl",reads.Sql[0]);
        var details=Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(2)).Model);Assert.True(details.IsCurrentlyFeatured);
        foreach(var value in new object[]{model,details}) {
            var json=JsonSerializer.Serialize(value);Assert.DoesNotContain("FeaturedFrom",json);Assert.DoesNotContain("FeaturedUntil",json);
            Assert.DoesNotContain("AmenitiesVerifiedAt",json);Assert.DoesNotContain("DataSourceId",json);
        }
        clock.Value=Now.AddSeconds(1);
        var expired=Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(2)).Model);Assert.False(expired.IsCurrentlyFeatured);
        Assert.True((await db.Places.FindAsync(2))!.IsFeatured); // No expiry mutation/job.
        var future=Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await controller.Details(4)).Model);Assert.True(future.IsCurrentlyFeatured);
        var map=new MapController(db,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,null!,clock:new Clock())
        {ControllerContext=new(){HttpContext=new DefaultHttpContext()}};
        await map.Index();var markers=Assert.IsAssignableFrom<IReadOnlyList<PlaceMapItem>>((object)map.ViewBag.ManagedPlaces);
        Assert.Equal(2,Assert.Single(markers,p=>p.IsCurrentlyFeatured).Id);
        Assert.DoesNotContain("FeaturedUntil",JsonSerializer.Serialize(markers));
    }
    [Fact]
    public async Task SavedOrderRemainsNewestFirstWithNoPromotion()
    {
        await using var db=await Context();db.Users.Add(new ApplicationUser{Id="u",UserName="u"});
        var old=new Place{Name="Featured old",Category=PlaceCategory.PetShop,IsFeatured=true};var recent=new Place{Name="Normal recent",Category=PlaceCategory.PetShop};
        db.Places.AddRange(old,recent);await db.SaveChangesAsync();
        db.SavedPlaces.AddRange(new SavedPlace{UserId="u",PlaceId=old.Id,SavedAt=Now.AddDays(-1)},new SavedPlace{UserId="u",PlaceId=recent.Id,SavedAt=Now});await db.SaveChangesAsync();
        var controller=new SavedPlacesController(db,new PlaceLogoCloudName("test")){ControllerContext=new(){HttpContext=new DefaultHttpContext{User=new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier,"u")],"Test"))}}};
        var model=Assert.IsType<PlaceDiscoveryViewModel>(Assert.IsType<ViewResult>(await controller.Index()).Model);
        Assert.Equal(new[]{recent.Id,old.Id},model.Places.Select(p=>p.Id));Assert.All(model.Places,p=>Assert.False(p.IsCurrentlyFeatured));
    }
    [Fact]
    public async Task FeaturedAndNormalGroupsRetainDeterministicNameAndIdOrder()
    {
        await using var db=await Context();
        db.Places.AddRange(new Place{Id=1,Name="Z",Category=PlaceCategory.PetShop,IsFeatured=true},
            new Place{Id=2,Name="A",Category=PlaceCategory.PetShop,IsFeatured=true},
            new Place{Id=3,Name="A",Category=PlaceCategory.PetShop,IsFeatured=true},
            new Place{Id=4,Name="A",Category=PlaceCategory.PetShop},new Place{Id=5,Name="A",Category=PlaceCategory.PetShop});
        await db.SaveChangesAsync();
        var model=Assert.IsType<PlaceDiscoveryViewModel>(Assert.IsType<ViewResult>(await Public(db).Index()).Model);
        Assert.Equal(new[]{2,3,1,4,5},model.Places.Select(p=>p.Id));
    }
    [Fact]
    public void MigrationHasOnlyThreeAdditiveColumnsAndModelMatchesSnapshot()
    {
        var migration=new AddPlaceFeatured();Assert.Equal(3,migration.UpOperations.Count);Assert.Equal(3,migration.DownOperations.Count);
        var columns=migration.UpOperations.Cast<AddColumnOperation>().ToArray();Assert.All(columns,c=>Assert.Equal("Places",c.Table));
        var flag=Assert.Single(columns,c=>c.Name=="IsFeatured");Assert.False(flag.IsNullable);Assert.Equal(false,flag.DefaultValue);
        foreach(var date in columns.Where(c=>c.Name!="IsFeatured")){Assert.True(date.IsNullable);Assert.Equal("timestamp with time zone",date.ColumnType);}
        Assert.Equal(columns.Select(c=>c.Name).Order(),migration.DownOperations.Cast<DropColumnOperation>().Select(c=>c.Name).Order());
        using var db=new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=model_only;Username=unused;Password=unused").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.True(db.Model.FindEntityType(typeof(Place))!.FindProperty("UpdatedAt")!.IsConcurrencyToken);
        var sql=db.Places.WithFeatured(Now).OrderByDescending(p=>p.IsCurrentlyFeatured).Select(p=>new{p.Place.Id,p.IsCurrentlyFeatured}).ToQueryString();
        Assert.Contains("ORDER BY",sql);Assert.Contains("FeaturedUntil",sql);Assert.DoesNotContain("AmenitiesSourceUrl",sql);
    }
    private sealed class Pause : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release {get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e,InterceptionResult<int> r,CancellationToken ct=default)
        {Reached.TrySetResult();await Release.Task.WaitAsync(TimeSpan.FromSeconds(20),ct);return r;}
    }
    private sealed class Reads : DbCommandInterceptor
    {
        public List<string> Sql {get;}=[];
        public List<(string Sql, Dictionary<string, object?> Parameters)> Commands {get;}=[];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c,CommandEventData e,InterceptionResult<DbDataReader> r,CancellationToken ct=default)
        {
            Sql.Add(c.CommandText);
            Commands.Add((c.CommandText, c.Parameters.Cast<DbParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value)));
            return ValueTask.FromResult(r);
        }
    }
    private sealed class MemoryTempData : ITempDataProvider
    {public IDictionary<string,object> LoadTempData(HttpContext c)=>new Dictionary<string,object>();public void SaveTempData(HttpContext c,IDictionary<string,object> v){}}
    public void Dispose(){if(File.Exists(file))File.Delete(file);}
}
