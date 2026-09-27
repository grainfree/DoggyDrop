using System.ComponentModel.DataAnnotations;
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

public sealed class DataProvenanceTests : IDisposable
{
    private readonly string file = Path.Combine(Path.GetTempPath(), $"doggydrop-provenance-{Guid.NewGuid():N}.db");
    private readonly SqlLog sql = new();
    private DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={file};Pooling=False").AddInterceptors(sql).Options;
    private async Task<ApplicationDbContext> Seed()
    {
        var db = new ApplicationDbContext(Options()); await db.Database.EnsureCreatedAsync();
        db.DataSources.AddRange(new DataSource { Name = "Občina", Type = DataSourceType.Municipality },
            new DataSource { Name = "Komunala", Type = DataSourceType.UtilityCompany });
        db.Users.Add(new ApplicationUser { Id = "contributor", UserName = "Contributor" });
        await db.SaveChangesAsync();
        for (var i = 1; i <= 2; i++)
        {
            db.TrashBins.Add(new TrashBin { Name = $"Koš {i}", Latitude = 46, Longitude = 15 + i * 0.00001,
                DataSourceId = 1, UserId = "contributor", IsApproved = i == 1 });
            db.Places.Add(new Place { Name = $"Lokacija {i}", Category = PlaceCategory.DogPark, Latitude = 46, Longitude = 15,
                DataSourceId = 1, AmenitiesSourceUrl = "https://example.com/facts", AmenitiesVerifiedAt = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Amenities = [new PlaceAmenity { AmenityType = PlaceAmenityType.Fenced }] });
        }
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); return db;
    }
    private T Setup<T>(T controller) where T : Controller
    {
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "admin"), new Claim(ClaimTypes.Role, "Admin")], "Test")) } };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new TempData()); return controller;
    }
    private AdminPlacesController Admin(ApplicationDbContext db) => Setup(new AdminPlacesController(db,
        new MissingPlaceLogoStorage(), new PlaceLogoReferenceReader(Options()), new PlaceLogoCloudName("test"), NullLogger<AdminPlacesController>.Instance));

    [Theory]
    [InlineData(DataSourceType.Manual, 1)] [InlineData(DataSourceType.Municipality, 2)]
    [InlineData(DataSourceType.UtilityCompany, 3)] [InlineData(DataSourceType.PublicDataset, 4)]
    [InlineData(DataSourceType.Partner, 5)] [InlineData(DataSourceType.UserContribution, 6)]
    public async Task StableTypesCreateEditAndKeepDatasetDateIndependent(DataSourceType type, int expected)
    {
        Assert.Equal(expected, (int)type);
        await using var db = await Seed(); var controller = Setup(new AdminDataSourcesController(db));
        var input = new DataSourceInput { Name = "  Nov vir  ", Type = type, DataDate = new DateOnly(2026, 8, 31) };
        Assert.IsType<RedirectToActionResult>(await controller.Create(input));
        var source = await db.DataSources.SingleAsync(s => s.Name == "Nov vir");
        Assert.Null(source.ContactName); Assert.Null(source.ContactEmail); Assert.Null(source.Notes); Assert.Null(source.WebsiteUrl);
        var created = source.CreatedAt; source.UpdatedAt = DateTime.UtcNow.AddDays(-1); await db.SaveChangesAsync();
        var old = source.UpdatedAt; input.Name = "Urejen vir"; input.ContactEmail = "contact@example.com";
        await controller.Edit(source.Id, input);
        Assert.True(source.UpdatedAt > old); Assert.Equal(created, source.CreatedAt);
        Assert.Equal(new DateOnly(2026, 8, 31), source.DataDate);
    }

    [Theory]
    [InlineData("javascript:alert(1)")] [InlineData("data:text/html,bad")] [InlineData("file:///tmp/source")]
    [InlineData("/relative")] [InlineData("https://")] [InlineData("https://user:pass@example.com")]
    public void UnsafeSourceUrlsFailValidation(string value)
    {
        var input = new DataSourceInput { Name = "Vir", Type = DataSourceType.Manual, WebsiteUrl = value };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), errors, true));
        Assert.Contains(errors, e => e.MemberNames.Contains(nameof(input.WebsiteUrl)));
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(7)]
    public void UnknownTypeFailsValidation(int value)
    {
        var input = new DataSourceInput { Name = "Vir", Type = (DataSourceType)value };
        Assert.False(Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    [Fact]
    public void MetadataBoundsAndPublicEntitySerializationAreSafe()
    {
        var invalid = new DataSourceInput { Name = new string('x', 161), Type = DataSourceType.Manual,
            ContactName = new string('x', 121), ContactEmail = "invalid", Notes = new string('x', 2001), WebsiteUrl = "https://example.com/" + new string('x', 500) };
        var errors = new List<ValidationResult>(); Assert.False(Validator.TryValidateObject(invalid, new ValidationContext(invalid), errors, true));
        Assert.Equal(5, errors.Count);
        var secret = new DataSource { Name = "Internal origin", ContactName = "Private contact", ContactEmail = "private@example.com", Notes = "Private notes" };
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(new Place { DataSource = secret }));
        Assert.DoesNotContain("Private", JsonSerializer.Serialize(new TrashBin { DataSource = secret }));
    }

    [Fact]
    public async Task DeletingSourceSetsBothForeignKeysNullAndPreservesContentAndAttribution()
    {
        await using var db = await Seed();
        Assert.IsType<RedirectToActionResult>(await Setup(new AdminDataSourcesController(db)).DeleteConfirmed(1));
        Assert.Equal(2, await db.TrashBins.CountAsync()); Assert.Equal(2, await db.Places.CountAsync());
        Assert.All(await db.TrashBins.AsNoTracking().ToListAsync(), b => { Assert.Null(b.DataSourceId); Assert.Equal("contributor", b.UserId); });
        Assert.All(await db.Places.AsNoTracking().ToListAsync(), p => { Assert.Null(p.DataSourceId); Assert.NotNull(p.AmenitiesVerifiedAt); });
        Assert.Equal(2, await db.PlaceAmenities.CountAsync());
    }

    [Theory]
    [InlineData(BulkTarget.Places, BulkAction.Activate)] [InlineData(BulkTarget.Places, BulkAction.Deactivate)]
    [InlineData(BulkTarget.Places, BulkAction.AssignSource)] [InlineData(BulkTarget.Places, BulkAction.ClearSource)]
    [InlineData(BulkTarget.Bins, BulkAction.AssignSource)] [InlineData(BulkTarget.Bins, BulkAction.ClearSource)]
    public async Task BulkActionsDeduplicateAndPreserveUnrelatedFacts(BulkTarget target, BulkAction action)
    {
        await using var db = await Seed();
        var input = new BulkInput { Target = target, Action = action, Ids = [1, 2, 1], DataSourceId = action == BulkAction.AssignSource ? 2 : null };
        Assert.Equal(2, await new AdminBulkTools(db).ApplyAsync(input));
        var places = await db.Places.AsNoTracking().ToListAsync(); var bins = await db.TrashBins.AsNoTracking().OrderBy(b => b.Id).ToListAsync();
        if (target == BulkTarget.Places)
        {
            if (action is BulkAction.Activate or BulkAction.Deactivate) Assert.All(places, p => Assert.Equal(action == BulkAction.Activate, p.IsActive));
            else Assert.All(places, p => Assert.Equal(action == BulkAction.AssignSource ? 2 : (int?)null, p.DataSourceId));
        }
        else Assert.All(bins, b => Assert.Equal(action == BulkAction.AssignSource ? 2 : (int?)null, b.DataSourceId));
        Assert.True(bins[0].IsApproved); Assert.False(bins[1].IsApproved); Assert.All(bins, b => Assert.Null(b.ApprovedAt));
        Assert.All(places, p => { Assert.Equal("https://example.com/facts", p.AmenitiesSourceUrl); Assert.NotNull(p.AmenitiesVerifiedAt); });
        Assert.Equal(2, await db.PlaceAmenities.CountAsync());
    }

    [Theory]
    [InlineData("empty")] [InlineData("negative")] [InlineData("missing")] [InlineData("too-many")]
    [InlineData("action")] [InlineData("target")] [InlineData("source")] [InlineData("bin-activate")]
    public async Task InvalidBulkSelectionCannotPartiallySave(string fault)
    {
        await using var db = await Seed(); var input = new BulkInput { Target = BulkTarget.Places, Action = BulkAction.AssignSource, Ids = [1, 2], DataSourceId = 2 };
        switch (fault)
        {
            case "empty": input.Ids = []; break; case "negative": input.Ids = [1, -1]; break;
            case "missing": input.Ids = [1, 999]; break; case "too-many": input.Ids = Enumerable.Repeat(1, 101).ToArray(); break;
            case "action": input.Action = (BulkAction)99; break; case "target": input.Target = (BulkTarget)99; break;
            case "source": input.DataSourceId = 999; break;
            case "bin-activate": input.Target = BulkTarget.Bins; input.Action = BulkAction.Activate; input.DataSourceId = null; break;
        }
        Assert.Null(await new AdminBulkTools(db).PreviewAsync(input));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AdminBulkTools(db).ApplyAsync(input));
        Assert.All(await db.Places.AsNoTracking().ToListAsync(), p => Assert.Equal(1, p.DataSourceId));
    }

    [Theory]
    [InlineData(BulkTarget.Places)] [InlineData(BulkTarget.Bins)]
    public async Task DatabaseFailureRollsBackEntireBulkSelection(BulkTarget target)
    {
        await using var db = await Seed();
        var trigger = target == BulkTarget.Places
            ? "CREATE TRIGGER fail_bulk BEFORE UPDATE ON Places WHEN NEW.Id = 2 BEGIN SELECT RAISE(ABORT, 'test failure'); END;"
            : "CREATE TRIGGER fail_bulk BEFORE UPDATE ON TrashBins WHEN NEW.Id = 2 BEGIN SELECT RAISE(ABORT, 'test failure'); END;";
        await db.Database.ExecuteSqlRawAsync(trigger);
        await Assert.ThrowsAsync<DbUpdateException>(() => new AdminBulkTools(db).ApplyAsync(new BulkInput { Target = target, Action = BulkAction.AssignSource, Ids = [1, 2], DataSourceId = 2 }));
        await using var read = new ApplicationDbContext(Options());
        Assert.All(await read.Places.ToListAsync(), p => Assert.Equal(1, p.DataSourceId));
        Assert.All(await read.TrashBins.ToListAsync(), b => Assert.Equal(1, b.DataSourceId));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task BulkAndAmenityEditsConflictAtomicallyInEitherOrder(bool delayBulk)
    {
        await using var seed = await Seed(); var pause = new PauseSave();
        await using var delayed = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>(Options()).AddInterceptors(pause).Options);
        await using var first = new ApplicationDbContext(Options());
        var edit = PlaceInput.FromPlace(await seed.Places.Include(p => p.Amenities).FirstAsync(), "test");
        edit.AmenityTypes = [PlaceAmenityType.WaterForDogs]; edit.VerifyAmenitiesToday = true;
        var bulk = new BulkInput { Target = BulkTarget.Places, Action = BulkAction.AssignSource, Ids = [1, 2], DataSourceId = 2 };
        Task pending = delayBulk ? new AdminBulkTools(delayed).ApplyAsync(bulk) : Admin(delayed).Edit(1, edit);
        try
        {
            await pause.Reached.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (delayBulk) Assert.IsType<RedirectToActionResult>(await Admin(first).Edit(1, edit));
            else Assert.Equal(2, await new AdminBulkTools(first).ApplyAsync(bulk));
        }
        finally { pause.Release.TrySetResult(); }
        if (delayBulk) await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => pending);
        else Assert.IsType<ViewResult>(await (Task<IActionResult>)pending);
        await using var read = new ApplicationDbContext(Options()); var places = await read.Places.Include(p => p.Amenities).OrderBy(p => p.Id).ToListAsync();
        Assert.All(places, p => Assert.Equal(delayBulk ? 1 : 2, p.DataSourceId));
        Assert.Equal(delayBulk ? PlaceAmenityType.WaterForDogs : PlaceAmenityType.Fenced, Assert.Single(places[0].Amenities).AmenityType);
        Assert.NotNull(places[0].AmenitiesVerifiedAt);
    }

    [Fact]
    public async Task SourceStatusAndCategoryFiltersComposeAndCountsUseOneQuery()
    {
        await using var db = await Seed();
        await new AdminBulkTools(db).ApplyAsync(new BulkInput { Target = BulkTarget.Places, Action = BulkAction.ClearSource, Ids = [2] });
        var admin = Admin(db);
        var list = Assert.IsAssignableFrom<IReadOnlyList<Place>>(Assert.IsType<ViewResult>(await admin.Index("active", PlaceCategory.DogPark, noSource: true)).Model);
        Assert.Equal(2, Assert.Single(list).Id);
        Assert.Empty(Assert.IsAssignableFrom<IReadOnlyList<Place>>(Assert.IsType<ViewResult>(await admin.Index("inactive", PlaceCategory.DogPark, 1)).Model));
        var bins = Assert.IsAssignableFrom<IReadOnlyList<AdminBinRow>>(Assert.IsType<ViewResult>(await Setup(new AdminBinsController(db)).Index("pending", 1)).Model);
        Assert.Equal(2, Assert.Single(bins).Id);
        sql.Commands.Clear();
        var sources = Assert.IsAssignableFrom<IReadOnlyList<DataSourceRow>>(Assert.IsType<ViewResult>(await Setup(new AdminDataSourcesController(db)).Index()).Model);
        Assert.Single(sql.Commands); Assert.Equal(2, sources.Single(s => s.Id == 1).Bins); Assert.Equal(1, sources.Single(s => s.Id == 1).Places);
    }

    [Fact]
    public async Task IndividualPlaceOriginEditPreservesVerificationAndRejectsInvalidSource()
    {
        await using var db = await Seed();
        var place = await db.Places.Include(p => p.Amenities).FirstAsync();
        var verified = place.AmenitiesVerifiedAt;
        var input = PlaceInput.FromPlace(place,"test"); input.DataSourceId = 2;
        Assert.IsType<RedirectToActionResult>(await Admin(db).Edit(place.Id,input));
        Assert.Equal(2,place.DataSourceId); Assert.Equal(verified,place.AmenitiesVerifiedAt);
        Assert.Equal(PlaceAmenityType.Fenced,Assert.Single(place.Amenities).AmenityType);
        input.DataSourceId = 999; input.Name = "Must not save";
        Assert.IsType<ViewResult>(await Admin(db).Edit(place.Id,input));
        var saved = await db.Places.AsNoTracking().SingleAsync(p=>p.Id==place.Id);
        Assert.Equal(2,saved.DataSourceId); Assert.NotEqual(input.Name,saved.Name); Assert.Equal(verified,saved.AmenitiesVerifiedAt);
    }

    [Fact]
    public async Task AdminListsLimitSelectionToOnePageAndRetainFilters()
    {
        await using var db = await Seed();
        db.TrashBins.AddRange(Enumerable.Range(0,101).Select(i=>new TrashBin { Name=$"Page bin {i}",DataSourceId=2,IsApproved=true }));
        db.Places.AddRange(Enumerable.Range(0,101).Select(i=>new Place { Name=$"Page place {i:D3}",DataSourceId=2,Category=PlaceCategory.PetShop }));
        await db.SaveChangesAsync();db.ChangeTracker.Clear();
        var bins = Setup(new AdminBinsController(db));
        var page1 = Assert.IsAssignableFrom<IReadOnlyList<AdminBinRow>>(Assert.IsType<ViewResult>(await bins.Index("approved",2)).Model);
        var page2 = Assert.IsAssignableFrom<IReadOnlyList<AdminBinRow>>(Assert.IsType<ViewResult>(await bins.Index("approved",2,page:2)).Model);
        Assert.Equal(100,page1.Count);Assert.Single(page2);Assert.Empty(page1.Select(p=>p.Id).Intersect(page2.Select(p=>p.Id)));
        var places = Admin(db);
        var places1 = Assert.IsAssignableFrom<IReadOnlyList<Place>>(Assert.IsType<ViewResult>(await places.Index("active",PlaceCategory.PetShop,2)).Model);
        var places2 = Assert.IsAssignableFrom<IReadOnlyList<Place>>(Assert.IsType<ViewResult>(await places.Index("active",PlaceCategory.PetShop,2,page:2)).Model);
        Assert.Equal(100,places1.Count);Assert.Single(places2);Assert.Empty(places1.Select(p=>p.Id).Intersect(places2.Select(p=>p.Id)));
    }

    [Fact]
    public async Task DuplicateDatabaseQueryIsBoundedAndReadOnly()
    {
        await using var db = await Seed(); sql.Commands.Clear();
        var results = await new DuplicateCandidates(db).FindAsync(BulkTarget.Bins, 46, 15, 1000);
        Assert.Single(results.Pairs); Assert.Single(sql.Commands); Assert.Contains("LIMIT", sql.Commands[0]); Assert.Contains("Latitude", sql.Commands[0]);
        Assert.Empty(db.ChangeTracker.Entries()); Assert.Equal(2, await db.TrashBins.CountAsync());
        db.TrashBins.AddRange(Enumerable.Range(0, DuplicateCandidates.MaxRecords).Select(i => new TrashBin { Name = "Dense", Latitude = 46, Longitude = 15 }));
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var dense = await new DuplicateCandidates(db).FindAsync(BulkTarget.Bins, 46, 15, 1000);
        Assert.True(dense.TooManyRecords); Assert.Equal(0, dense.Comparisons); Assert.Empty(dense.Pairs);
    }

    [Fact]
    public void DuplicateSignalsAreConservativeAndHandleGeographicEdges()
    {
        DuplicateRecord Row(int id, string name, double lat, double lon) => new(id, name, lat, lon, null, true);
        var bins = DuplicateCandidates.Scan([Row(1,"A",46,15),Row(2,"B",46.0001,15),Row(3,"C",46.001,15)], BulkTarget.Bins);
        Assert.Single(bins.Pairs);
        var places = DuplicateCandidates.Scan([Row(1,"  Dog   Cafe ",46,15),Row(2,"dog cafe",46.0005,15),Row(3,"Dog cafe",47,15),Row(4,"Other",46,15)], BulkTarget.Places);
        Assert.Single(places.Pairs); Assert.InRange(places.Pairs[0].Metres, 50, 60);
        Assert.Single(DuplicateCandidates.Scan([Row(1,"A",0,179.99999),Row(2,"B",0,-179.99999)], BulkTarget.Bins).Pairs);
        Assert.Single(DuplicateCandidates.Scan([Row(1,"A",89.99999,0),Row(2,"B",89.99999,180)], BulkTarget.Bins).Pairs);
    }

    [Fact]
    public void TenThousandRecordsUseLocalBucketsAndDenseResultsAreCapped()
    {
        var rows = Enumerable.Range(0, 10000).Select(i => new DuplicateRecord(i, "Same name", -70 + i * .01, 15, null, true)).ToArray();
        foreach (var target in Enum.GetValues<BulkTarget>())
        {
            var result = DuplicateCandidates.Scan(rows, target);
            Assert.Empty(result.Pairs); Assert.True(result.Comparisons < rows.Length * 10); Assert.False(result.Limited);
        }
        var dense = DuplicateCandidates.Scan(rows.Select(r => r with { Latitude = 46 }), BulkTarget.Bins);
        Assert.True(dense.Limited); Assert.Equal(DuplicateCandidates.MaxPairs, dense.Pairs.Count); Assert.True(dense.Comparisons <= DuplicateCandidates.MaxComparisons);
    }

    [Fact]
    public void MigrationIsOnlyAdditiveOriginSchemaWithSetNullAndMatchingSnapshot()
    {
        var migration = new AddDataSources();
        Assert.Equal(7, migration.UpOperations.Count); Assert.Equal(7, migration.DownOperations.Count);
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()); Assert.Equal("DataSources", table.Name);
        Assert.Equal("date", table.Columns.Single(c => c.Name == "DataDate").ColumnType);
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), c => { Assert.Equal("DataSourceId", c.Name); Assert.True(c.IsNullable); });
        Assert.Equal(2, migration.UpOperations.OfType<CreateIndexOperation>().Count());
        Assert.All(migration.UpOperations.OfType<AddForeignKeyOperation>(), fk => Assert.Equal(ReferentialAction.SetNull, fk.OnDelete));
        Assert.Equal("DataSources", Assert.Single(migration.DownOperations.OfType<DropTableOperation>()).Name);
        using var pg = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=127.0.0.1;Port=1;Database=model_only;Username=unused;Password=unused").Options);
        Assert.False(pg.Database.HasPendingModelChanges());
    }

    public void Dispose() { if (File.Exists(file)) File.Delete(file); }
    private sealed class TempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private sealed class PauseSave : SaveChangesInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        { Reached.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken); return result; }
    }
    private sealed class SqlLog : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Commands.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
