using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Migrations;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PlaceAmenitiesTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"doggydrop-amenities-{Guid.NewGuid():N}.db");
    private readonly ReadCounter counter = new();
    private DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={file};Pooling=False").AddInterceptors(counter).Options;
    private async Task<ApplicationDbContext> Context()
    {
        var db = new ApplicationDbContext(Options());
        await db.Database.EnsureCreatedAsync();
        return db;
    }
    private AdminPlacesController Admin(ApplicationDbContext db)
    {
        var controller = new AdminPlacesController(db, new MissingPlaceLogoStorage(), new PlaceLogoReferenceReader(Options()),
            new PlaceLogoCloudName("test"), NullLogger<AdminPlacesController>.Instance);
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext
        { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test")) } };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new MemoryTempData());
        return controller;
    }
    private static PlacesController Public(ApplicationDbContext db) => new(db, new PlaceLogoCloudName("test"))
    { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
    private static PlaceInput Input(PlaceCategory category = PlaceCategory.DogPark) => new()
    { Name = "Test place", Category = category, Latitude = 46.05, Longitude = 14.51 };
    private static PlaceDetailsViewModel Details(IActionResult result) => Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(result).Model);

    [Theory]
    [InlineData(PlaceAmenityType.DogsInside, 1, "Psi dobrodošli v notranjosti")]
    [InlineData(PlaceAmenityType.DogsTerrace, 2, "Psi dobrodošli na terasi")]
    [InlineData(PlaceAmenityType.WaterForDogs, 3, "Voda za pse")]
    [InlineData(PlaceAmenityType.Fenced, 4, "Ograjeno")]
    [InlineData(PlaceAmenityType.OffLeash, 5, "Prosto gibanje brez povodca")]
    [InlineData(PlaceAmenityType.DogsInWater, 6, "Kopanje psov dovoljeno")]
    [InlineData(PlaceAmenityType.DogShower, 7, "Tuš za pse")]
    [InlineData(PlaceAmenityType.WasteBins, 8, "Koš za pasje iztrebke")]
    public async Task EveryStableAmenityCanBeCreatedPreselectedAndRemoved(PlaceAmenityType type, int value, string label)
    {
        Assert.Equal(value, (int)type);
        var presentation = Assert.Single(PlaceAmenities.All, item => item.Type == type);
        Assert.Equal(label, presentation.Label);
        Assert.StartsWith("bi-", presentation.IconClass);
        await using var db = await Context();
        var input = Input();
        input.AmenityTypes = [type, type];
        Assert.IsType<RedirectToActionResult>(await Admin(db).Create(input));
        var place = await db.Places.SingleAsync();
        Assert.Equal(type, (await db.PlaceAmenities.SingleAsync()).AmenityType);
        var edit = Assert.IsType<PlaceInput>(Assert.IsType<ViewResult>(await Admin(db).Edit(place.Id)).Model);
        Assert.Equal([type], edit.AmenityTypes);
        Assert.Equal(label, Assert.Single(Details(await Public(db).Details(place.Id)).Amenities).Label);
        edit.AmenityTypes.Clear();
        Assert.IsType<RedirectToActionResult>(await Admin(db).Edit(place.Id, edit));
        Assert.Empty(await db.PlaceAmenities.ToListAsync());
    }

    [Theory]
    [InlineData(PlaceCategory.Veterinarian)]
    [InlineData(PlaceCategory.PetShop)]
    [InlineData(PlaceCategory.Groomer)]
    [InlineData(PlaceCategory.DogSchool)]
    [InlineData(PlaceCategory.DogFriendlyCafe)]
    [InlineData(PlaceCategory.DogPark)]
    [InlineData(PlaceCategory.DogBeach)]
    public async Task AllCategoriesStartUnknownAndCategoryChangesRetainExplicitFacts(PlaceCategory category)
    {
        await using var db = await Context();
        await Admin(db).Create(Input(category));
        var place = await db.Places.SingleAsync();
        Assert.Empty(Details(await Public(db).Details(place.Id)).Amenities);
        var edit = PlaceInput.FromPlace(place, "test");
        edit.AmenityTypes = [PlaceAmenityType.DogsTerrace];
        await Admin(db).Edit(place.Id, edit);
        edit = Assert.IsType<PlaceInput>(Assert.IsType<ViewResult>(await Admin(db).Edit(place.Id)).Model);
        edit.Category = category == PlaceCategory.Veterinarian ? PlaceCategory.DogBeach : PlaceCategory.Veterinarian;
        await Admin(db).Edit(place.Id, edit);
        Assert.Equal(PlaceAmenityType.DogsTerrace, Assert.Single(Details(await Public(db).Details(place.Id)).Amenities).Type);
        Assert.Single(await db.PlaceAmenities.ToListAsync());
    }

    [Fact]
    public async Task EditingDiffsAllEightWithoutDuplicatesAndPreservesUnchangedVerification()
    {
        await using var db = await Context();
        var input = Input();
        input.AmenityTypes = Enum.GetValues<PlaceAmenityType>().ToList();
        input.AmenitiesSourceUrl = "https://official.example/dogs";
        input.VerifyAmenitiesToday = true;
        var before = DateTime.UtcNow;
        await Admin(db).Create(input);
        var place = await db.Places.SingleAsync();
        Assert.InRange(place.AmenitiesVerifiedAt!.Value, before, DateTime.UtcNow);
        Assert.Equal(DateTimeKind.Utc, place.AmenitiesVerifiedAt.Value.Kind);
        var verified = place.AmenitiesVerifiedAt;
        var edit = PlaceInput.FromPlace(place, "test");
        edit.Name = "Updated name";
        edit.AmenityTypes.AddRange(edit.AmenityTypes.ToArray());
        await Admin(db).Edit(place.Id, edit);
        Assert.Equal(8, await db.PlaceAmenities.CountAsync());
        Assert.Equal(verified, place.AmenitiesVerifiedAt);
        edit.AmenityTypes = [PlaceAmenityType.WaterForDogs, PlaceAmenityType.WasteBins];
        await Admin(db).Edit(place.Id, edit);
        Assert.Equal(2, await db.PlaceAmenities.CountAsync());
        Assert.Null(place.AmenitiesVerifiedAt);
        edit.VerifyAmenitiesToday = true;
        await Admin(db).Edit(place.Id, edit);
        Assert.NotNull(place.AmenitiesVerifiedAt);
        edit.VerifyAmenitiesToday = false;
        edit.AmenitiesSourceUrl = "http://another.example/info";
        await Admin(db).Edit(place.Id, edit);
        Assert.Null(place.AmenitiesVerifiedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(-1)]
    public async Task UnknownValuesRejectCreateAndEditWithoutPartialChanges(int value)
    {
        await using var db = await Context();
        var bad = Input();
        bad.AmenityTypes = [(PlaceAmenityType)value];
        var create = Admin(db);
        Assert.IsType<ViewResult>(await create.Create(bad));
        Assert.False(create.ModelState.IsValid);
        Assert.Empty(await db.Places.ToListAsync());
        await Admin(db).Create(Input());
        var place = await db.Places.SingleAsync();
        bad.Name = "Must not be saved";
        var edit = Admin(db);
        Assert.IsType<ViewResult>(await edit.Edit(place.Id, bad));
        Assert.False(edit.ModelState.IsValid);
        Assert.Equal("Test place", (await db.Places.AsNoTracking().SingleAsync()).Name);
        Assert.Empty(await db.PlaceAmenities.ToListAsync());
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,test")]
    [InlineData("file:///tmp/data")]
    [InlineData("https://")]
    [InlineData("not a URL")]
    [InlineData("https://user:pass@example.com")]
    [InlineData("https://example.com\\evil")]
    [InlineData("https://example.com/with space")]
    public void UnsafeSourcesAreRejected(string source)
    {
        var input = Input();
        input.AmenitiesSourceUrl = source;
        Assert.Contains(input.Validate(), error => error.Field == nameof(PlaceInput.AmenitiesSourceUrl));
    }

    [Fact]
    public void SourceIsOptionalBoundedAndVerificationTimestampCannotBeBound()
    {
        foreach (var source in new[] { null, "", "  ", "https://example.com/info", "http://example.com/info" })
        {
            var input = Input(); input.AmenitiesSourceUrl = source;
            Assert.Empty(input.Validate());
        }
        var longSource = Input(); longSource.AmenitiesSourceUrl = "https://example.com/" + new string('a', 500);
        Assert.Contains(longSource.Validate(), error => error.Field == nameof(PlaceInput.AmenitiesSourceUrl));
        Assert.Single(typeof(PlaceInput).GetProperty(nameof(PlaceInput.AmenitiesVerifiedAt))!.GetCustomAttributes(typeof(BindNeverAttribute), true));
        Assert.Null(typeof(PlaceInput).GetProperty("PlaceId"));
        Assert.Null(typeof(PlaceInput).GetProperty("UserId"));
        Assert.Null(typeof(PlaceInput).GetProperty("Id"));
        var auth = Assert.IsType<AuthorizeAttribute>(Assert.Single(typeof(AdminPlacesController).GetCustomAttributes(typeof(AuthorizeAttribute), true)));
        Assert.Equal("Admin", auth.Roles);
        foreach (var method in typeof(AdminPlacesController).GetMethods().Where(m => m.GetCustomAttributes(typeof(HttpPostAttribute), true).Any()))
            Assert.Single(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
    }

    [Fact]
    public async Task PublicDetailsOnlyExposeSupportedPositiveFactsAndKeepInactivePlacesHidden()
    {
        await using var db = await Context();
        var input = Input(PlaceCategory.DogFriendlyCafe);
        input.AmenityTypes = [PlaceAmenityType.DogsTerrace];
        input.AmenitiesSourceUrl = "https://internal.example/verification";
        input.VerifyAmenitiesToday = true;
        await Admin(db).Create(input);
        var place = await db.Places.SingleAsync();
        db.PlaceAmenities.Add(new PlaceAmenity { PlaceId = place.Id, AmenityType = (PlaceAmenityType)99 });
        await db.SaveChangesAsync();
        var details = Details(await Public(db).Details(place.Id));
        Assert.Equal(PlaceAmenityType.DogsTerrace, Assert.Single(details.Amenities).Type);
        var json = JsonSerializer.Serialize(details);
        Assert.DoesNotContain("AmenitiesSourceUrl", json);
        Assert.DoesNotContain("AmenitiesVerifiedAt", json);
        Assert.DoesNotContain("internal.example", json);
        Assert.Null(typeof(PlaceMapItem).GetProperty("Amenities"));
        Assert.Null(typeof(PlaceDiscoveryItem).GetProperty("Amenities"));
        place.IsActive = false; await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Public(db).Details(place.Id));
        place.IsActive = true; place.Category = (PlaceCategory)99; await db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await Public(db).Details(place.Id));
    }

    [Fact]
    public async Task DetailsAndAdminEachLoadAmenitiesOnceAndDiscoveryDoesNotQueryThem()
    {
        await using var db = await Context();
        var input = Input(); input.AmenityTypes = Enum.GetValues<PlaceAmenityType>().ToList();
        await Admin(db).Create(input);
        var id = (await db.Places.SingleAsync()).Id;
        db.ChangeTracker.Clear(); counter.Sql.Clear();
        Assert.Equal(8, Details(await Public(db).Details(id)).Amenities.Count);
        Assert.Single(counter.Sql);
        Assert.Contains("PlaceAmenities", counter.Sql[0]);
        Assert.DoesNotContain("AmenitiesSourceUrl", counter.Sql[0]);
        Assert.DoesNotContain("AmenitiesVerifiedAt", counter.Sql[0]);
        Assert.DoesNotContain("DataSources", counter.Sql[0]);
        Assert.Empty(db.ChangeTracker.Entries());
        counter.Sql.Clear();
        await Admin(db).Edit(id);
        Assert.Equal(2, counter.Sql.Count); // One amenity load plus the source dropdown, never one query per checkbox.
        Assert.Single(counter.Sql, sql => sql.Contains("PlaceAmenities"));
        Assert.Single(counter.Sql, sql => sql.Contains("DataSources"));
        counter.Sql.Clear();
        await Public(db).Index();
        Assert.Single(counter.Sql);
        Assert.DoesNotContain("PlaceAmenities", counter.Sql[0]);
        Assert.DoesNotContain("AmenitiesSourceUrl", counter.Sql[0]);
    }

    [Theory]
    [InlineData(true, false)] // Review reproduction: the stale unconfirmed edit must fail.
    [InlineData(false, true)] // A stale confirmation must not certify a merged fact set.
    public async Task OverlappingAmenityEditsRejectStaleFactsAndVerification(bool firstConfirms, bool delayedConfirms)
    {
        await using var seed = await Context();
        await Admin(seed).Create(Input());
        var first = Input(); first.AmenityTypes = [PlaceAmenityType.Fenced]; first.VerifyAmenitiesToday = firstConfirms;
        var delayed = Input(); delayed.AmenityTypes = [PlaceAmenityType.WaterForDogs]; delayed.VerifyAmenitiesToday = delayedConfirms;
        delayed.Name = "Must not partially commit";
        var final = await AssertOverlappingConflict(first, delayed);
        Assert.Equal(PlaceAmenityType.Fenced, Assert.Single(final.Amenities).AmenityType);
        Assert.Equal(firstConfirms, final.AmenitiesVerifiedAt.HasValue);
        Assert.Equal(first.Name, final.Name);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OverlappingSourceEditsRejectStaleSourceAndVerification(bool firstConfirms, bool delayedConfirms)
    {
        await using var seed = await Context();
        var initial = Input(); initial.AmenityTypes = [PlaceAmenityType.Fenced];
        initial.AmenitiesSourceUrl = "https://example.com/original"; initial.VerifyAmenitiesToday = true;
        await Admin(seed).Create(initial);
        var first = PlaceInput.FromPlace(await seed.Places.Include(p => p.Amenities).SingleAsync(), "test");
        var delayed = PlaceInput.FromPlace(await seed.Places.Include(p => p.Amenities).SingleAsync(), "test");
        first.AmenitiesSourceUrl = "https://example.com/first"; first.VerifyAmenitiesToday = firstConfirms;
        delayed.AmenitiesSourceUrl = "https://example.com/stale"; delayed.VerifyAmenitiesToday = delayedConfirms;
        var final = await AssertOverlappingConflict(first, delayed);
        Assert.Equal(first.AmenitiesSourceUrl, final.AmenitiesSourceUrl);
        Assert.Equal(firstConfirms, final.AmenitiesVerifiedAt.HasValue);
        Assert.Equal(PlaceAmenityType.Fenced, Assert.Single(final.Amenities).AmenityType);
    }

    [Fact]
    public async Task OverlappingUnrelatedEditsConflictWithoutInvalidatingVerificationAndCanBeReappliedAfterReload()
    {
        await using var seed = await Context();
        var initial = Input(); initial.AmenityTypes = [PlaceAmenityType.Fenced]; initial.VerifyAmenitiesToday = true;
        await Admin(seed).Create(initial);
        var original = await seed.Places.Include(p => p.Amenities).SingleAsync();
        var first = PlaceInput.FromPlace(original, "test"); first.Name = "First name edit";
        var delayed = PlaceInput.FromPlace(original, "test"); delayed.Phone = "+38612345678";
        var final = await AssertOverlappingConflict(first, delayed);
        Assert.Equal(original.AmenitiesVerifiedAt, final.AmenitiesVerifiedAt);
        Assert.Null(final.Phone);
        await using var reloaded = new ApplicationDbContext(Options());
        var retry = PlaceInput.FromPlace(final, "test"); retry.Phone = delayed.Phone;
        Assert.IsType<RedirectToActionResult>(await Admin(reloaded).Edit(final.Id, retry));
        var saved = await reloaded.Places.AsNoTracking().SingleAsync();
        Assert.Equal(first.Name, saved.Name);
        Assert.Equal(delayed.Phone, saved.Phone);
        Assert.Equal(original.AmenitiesVerifiedAt, saved.AmenitiesVerifiedAt);
    }

    [Fact]
    public async Task ConflictingRemovalRollsBackAmenityDeletesAndScalarChanges()
    {
        await using var seed = await Context();
        var initial = Input(); initial.AmenityTypes = [PlaceAmenityType.Fenced]; initial.VerifyAmenitiesToday = true;
        await Admin(seed).Create(initial);
        var original = await seed.Places.Include(p => p.Amenities).SingleAsync();
        var first = PlaceInput.FromPlace(original, "test"); first.Name = "Winning unrelated edit";
        var delayed = PlaceInput.FromPlace(original, "test");
        delayed.AmenityTypes = [PlaceAmenityType.WaterForDogs];
        delayed.AmenitiesSourceUrl = "https://example.com/must-roll-back";
        var final = await AssertOverlappingConflict(first, delayed);
        Assert.Equal(original.AmenitiesVerifiedAt, final.AmenitiesVerifiedAt);
        Assert.Equal(PlaceAmenityType.Fenced, Assert.Single(final.Amenities).AmenityType);
        Assert.Null(final.AmenitiesSourceUrl);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActivationAndAmenityEditsShareTheConcurrencyBoundary(bool delayActivation)
    {
        await using var seed = await Context();
        await Admin(seed).Create(Input());
        var pause = new PauseBeforeSave();
        await using var delayedDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(Options())
            .AddInterceptors(pause).Options);
        await using var firstDb = new ApplicationDbContext(Options());
        var delayed = Admin(delayedDb);
        var input = Input(); input.AmenityTypes = [PlaceAmenityType.Fenced]; input.VerifyAmenitiesToday = true;
        var pending = delayActivation ? delayed.SetActive(1, false) : delayed.Edit(1, input);
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            var first = Admin(firstDb);
            Assert.IsType<RedirectToActionResult>(delayActivation ? await first.Edit(1, input) : await first.SetActive(1, false));
        }
        finally { pause.Release.TrySetResult(); }
        var result = await pending;
        if (delayActivation)
        {
            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Lokacija je bila med urejanjem spremenjena. Osveži podatke in poskusi znova.", delayed.TempData["PlaceSuccess"]);
        }
        else Assert.IsType<ViewResult>(result);
        await using var read = new ApplicationDbContext(Options());
        var final = await read.Places.AsNoTracking().Include(p => p.Amenities).SingleAsync();
        Assert.Equal(delayActivation, final.IsActive);
        Assert.Equal(delayActivation, final.AmenitiesVerifiedAt.HasValue);
        Assert.Equal(delayActivation ? 1 : 0, final.Amenities.Count);
    }

    [Fact]
    public async Task UpdatesAdvanceExistingTokenAtPostgresPrecisionEvenAfterClockRollback()
    {
        await using var db = await Context();
        await Admin(db).Create(Input());
        var place = await db.Places.SingleAsync();
        Assert.Equal(0, place.UpdatedAt.Ticks % 10);
        var future = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(9);
        place.UpdatedAt = future;
        await db.SaveChangesAsync();
        Assert.IsType<RedirectToActionResult>(await Admin(db).Edit(place.Id, Input()));
        Assert.True(place.UpdatedAt > future);
        Assert.Equal(0, place.UpdatedAt.Ticks % 10);
        var editVersion = place.UpdatedAt;
        await Admin(db).SetActive(place.Id, false);
        Assert.True(place.UpdatedAt > editVersion);
        Assert.Equal(0, place.UpdatedAt.Ticks % 10);
        Assert.True(db.Model.FindEntityType(typeof(Place))!.FindProperty(nameof(Place.UpdatedAt))!.IsConcurrencyToken);
    }

    private async Task<Place> AssertOverlappingConflict(PlaceInput firstInput, PlaceInput delayedInput)
    {
        var pause = new PauseBeforeSave();
        await using var delayedDb = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(Options())
            .AddInterceptors(pause).Options);
        await using var firstDb = new ApplicationDbContext(Options());
        var delayedController = Admin(delayedDb);
        var pending = delayedController.Edit(1, delayedInput);
        Place winner;
        try
        {
            // Both controller lifecycles read the same version before either commits.
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.IsType<RedirectToActionResult>(await Admin(firstDb).Edit(1, firstInput));
            winner = await firstDb.Places.AsNoTracking().Include(p => p.Amenities).SingleAsync();
        }
        finally { pause.Release.TrySetResult(); }
        Assert.IsType<ViewResult>(await pending);
        Assert.Contains(delayedController.ModelState[string.Empty]!.Errors,
            error => error.ErrorMessage == "Lokacija je bila med urejanjem spremenjena. Osveži podatke in poskusi znova.");
        await using var read = new ApplicationDbContext(Options());
        var final = await read.Places.AsNoTracking().Include(p => p.Amenities).SingleAsync();
        Assert.Equal(winner.UpdatedAt, final.UpdatedAt);
        Assert.Equal(winner.Name, final.Name);
        Assert.Equal(winner.AmenitiesSourceUrl, final.AmenitiesSourceUrl);
        Assert.Equal(winner.AmenitiesVerifiedAt, final.AmenitiesVerifiedAt);
        Assert.Equal(winner.Amenities.Select(a => a.AmenityType).Order(), final.Amenities.Select(a => a.AmenityType).Order());
        return final;
    }

    private sealed class PauseBeforeSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            Reached.TrySetResult();
            await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAmenityInsertRollsBackPlaceAndRelationsTogether(bool edit)
    {
        await using var db = await Context();
        if (edit)
        {
            var original = Input(); original.AmenityTypes = [PlaceAmenityType.Fenced];
            await Admin(db).Create(original);
        }
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER fail_amenity BEFORE INSERT ON PlaceAmenities
            WHEN NEW.AmenityType = 3 BEGIN SELECT RAISE(ABORT, 'test failure'); END;
            """);
        var input = Input(); input.Name = "Must roll back"; input.AmenityTypes = [PlaceAmenityType.WaterForDogs];
        input.VerifyAmenitiesToday = true; input.AmenitiesSourceUrl = "https://example.com/new";
        var controller = Admin(db);
        var result = edit ? await controller.Edit(1, input) : await controller.Create(input);
        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        await using var fresh = new ApplicationDbContext(Options());
        if (edit)
        {
            Assert.Equal("Test place", (await fresh.Places.SingleAsync()).Name);
            Assert.Null((await fresh.Places.SingleAsync()).AmenitiesVerifiedAt);
            Assert.Null((await fresh.Places.SingleAsync()).AmenitiesSourceUrl);
            Assert.Equal(PlaceAmenityType.Fenced, (await fresh.PlaceAmenities.SingleAsync()).AmenityType);
        }
        else { Assert.Empty(await fresh.Places.ToListAsync()); Assert.Empty(await fresh.PlaceAmenities.ToListAsync()); }
    }

    [Fact]
    public async Task CompositeKeyRejectsDuplicatesAndDeletingPlaceCascades()
    {
        await using var db = await Context();
        var input = Input(); input.AmenityTypes = [PlaceAmenityType.Fenced];
        await Admin(db).Create(input);
        db.ChangeTracker.Clear();
        db.PlaceAmenities.Add(new PlaceAmenity { PlaceId = 1, AmenityType = PlaceAmenityType.Fenced });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await db.Places.Where(p => p.Id == 1).ExecuteDeleteAsync();
        Assert.Empty(await db.PlaceAmenities.ToListAsync());
    }

    [Fact]
    public void MigrationAndSnapshotContainOnlyExpectedAdditions()
    {
        var migration = new AddPlaceAmenities();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("PlaceAmenities", table.Name);
        Assert.Equal(["PlaceId", "AmenityType"], table.PrimaryKey!.Columns);
        Assert.Equal(2, table.Columns.Count);
        var fk = Assert.Single(table.ForeignKeys);
        Assert.Equal("Places", fk.PrincipalTable);
        Assert.Equal(ReferentialAction.Cascade, fk.OnDelete);
        var columns = migration.UpOperations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(["AmenitiesSourceUrl", "AmenitiesVerifiedAt"], columns.Select(c => c.Name));
        Assert.All(columns, column => { Assert.Equal("Places", column.Table); Assert.True(column.IsNullable); });
        Assert.Equal(500, columns.Single(c => c.Name == "AmenitiesSourceUrl").MaxLength);
        Assert.Equal("timestamp with time zone", columns.Single(c => c.Name == "AmenitiesVerifiedAt").ColumnType);
        Assert.Equal(3, migration.UpOperations.Count);
        Assert.Equal(3, migration.DownOperations.Count);
        Assert.Equal("PlaceAmenities", Assert.Single(migration.DownOperations.OfType<DropTableOperation>()).Name);
        Assert.Equal(columns.Select(c => c.Name), migration.DownOperations.OfType<DropColumnOperation>().Select(c => c.Name));
        using var postgresModel = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=model_only;Username=unused;Password=unused").Options);
        Assert.False(postgresModel.Database.HasPendingModelChanges()); // Model comparison only; no connection.
    }

    public void Dispose() { if (File.Exists(file)) File.Delete(file); }
    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class ReadCounter : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
