using System.Security.Claims;
using System.Text.Json;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class PlacesTests : IDisposable
{
    private readonly string _db = Path.Combine(Path.GetTempPath(), $"doggydrop-places-{Guid.NewGuid():N}.db");

    [Theory]
    [InlineData(PlaceCategory.Veterinarian, "Veterinar")]
    [InlineData(PlaceCategory.PetShop, "Trgovina za male živali")]
    public void SupportedCategories_AreValidAndHaveSlovenianLabels(PlaceCategory category, string label)
    {
        Assert.Empty(Input(category).Validate());
        Assert.Equal(label, PlaceCategories.Label(category));
    }

    [Fact]
    public void ValidationRejectsUnknownCategoryInvalidCoordinatesAndUnsafeUrls()
    {
        Assert.Contains(Input((PlaceCategory)99).Validate(), error => error.Field == nameof(PlaceInput.Category));
        foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -91d, 91d })
        {
            var input = Input();
            input.Latitude = invalid;
            Assert.Contains(input.Validate(), error => error.Field == nameof(PlaceInput.Latitude));
        }
        foreach (var invalid in new[] { double.NaN, double.NegativeInfinity, -181d, 181d })
        {
            var input = Input();
            input.Longitude = invalid;
            Assert.Contains(input.Validate(), error => error.Field == nameof(PlaceInput.Longitude));
        }
        foreach (var invalid in new[] { "javascript:alert(1)", "data:text/html,test", "https://user@example.com", "https://example.com\\@evil.test" })
        {
            var input = Input();
            input.WebsiteUrl = invalid;
            Assert.Contains(input.Validate(), error => error.Field == nameof(PlaceInput.WebsiteUrl));
        }
        var image = Input();
        image.ImageUrl = "http://example.com/photo.jpg";
        Assert.Contains(image.Validate(), error => error.Field == nameof(PlaceInput.ImageUrl));
        image.ImageUrl = "javascript:alert(1)";
        Assert.Contains(image.Validate(), error => error.Field == nameof(PlaceInput.ImageUrl));
        image.ImageUrl = "https://example.com/photo.jpg";
        Assert.DoesNotContain(image.Validate(), error => error.Field == nameof(PlaceInput.ImageUrl));
        Assert.Empty(image.Validate());
        Assert.Null(PlaceLinks.TelephoneHref("+386;alert(1)"));
        Assert.Equal("tel:+38640123456", PlaceLinks.TelephoneHref("+386 (40) 123-456"));
    }

    [Fact]
    public void OptionalFieldsAreNotRequired_ButLengthsAreBounded()
    {
        Assert.Empty(Input().Validate());
        var longName = Input();
        longName.Name = new string('x', 121);
        Assert.Contains(longName.Validate(), error => error.Field == nameof(PlaceInput.Name));
        var longDescription = Input();
        longDescription.Description = new string('x', 2001);
        Assert.Contains(longDescription.Validate(), error => error.Field == nameof(PlaceInput.Description));
    }

    [Fact]
    public async Task AdminMutationsRequireRoleAndAntiforgery()
    {
        var authorization = Assert.Single(typeof(AdminPlacesController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true));
        Assert.Equal("Admin", ((AuthorizeAttribute)authorization).Roles);
        var mutations = typeof(AdminPlacesController).GetMethods()
            .Where(method => (method.Name is nameof(AdminPlacesController.Create) or nameof(AdminPlacesController.Edit) or nameof(AdminPlacesController.SetActive)) &&
                method.GetCustomAttributes(typeof(HttpPostAttribute), inherit: true).Any())
            .ToArray();
        Assert.Equal(3, mutations.Length);
        foreach (var method in mutations)
        {
            Assert.NotEmpty(method.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), inherit: true));
            Assert.Empty(method.GetCustomAttributes(typeof(AllowAnonymousAttribute), inherit: true));
        }

        using var services = new ServiceCollection().AddLogging().AddAuthorization().BuildServiceProvider();
        var provider = services.GetRequiredService<IAuthorizationPolicyProvider>();
        var policy = await AuthorizationPolicy.CombineAsync(provider, [(IAuthorizeData)authorization]);
        var authorizationService = services.GetRequiredService<IAuthorizationService>();
        var ordinaryUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "User")], "Test"));
        var adminUser = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"));
        Assert.False((await authorizationService.AuthorizeAsync(ordinaryUser, policy!)).Succeeded);
        Assert.True((await authorizationService.AuthorizeAsync(adminUser, policy!)).Succeeded);
    }

    [Fact]
    public async Task AdminCreateEditDeactivateReactivate_RetainsPlaceWithoutMassAssignment()
    {
        await using var db = await ContextAsync();
        var admin = Admin(db);
        var first = Input(PlaceCategory.Veterinarian);
        first.WebsiteUrl = "https://example.com/vet";
        first.ImageUrl = "https://example.com/vet.jpg";
        Assert.IsType<RedirectToActionResult>(await admin.Create(first));

        var place = await db.Places.SingleAsync();
        Assert.True(place.IsActive);
        Assert.Equal(PlaceCategory.Veterinarian, place.Category);
        var createdAt = place.CreatedAt;
        var placeId = place.Id;

        var edited = Input(PlaceCategory.PetShop);
        edited.Name = "Pet shop";
        edited.Latitude = 46.2;
        Assert.IsType<RedirectToActionResult>(await admin.Edit(placeId, edited));
        place = await db.Places.SingleAsync();
        Assert.Equal(PlaceCategory.PetShop, place.Category);
        Assert.Equal("Pet shop", place.Name);
        Assert.Equal(createdAt, place.CreatedAt);
        Assert.True(place.IsActive);

        await admin.SetActive(placeId, false);
        Assert.False((await db.Places.SingleAsync()).IsActive);
        Assert.IsType<ViewResult>(await admin.Edit(placeId));
        await admin.SetActive(placeId, true);
        Assert.True((await db.Places.SingleAsync()).IsActive);
        Assert.Equal(1, await db.Places.CountAsync());
    }

    [Fact]
    public async Task AdminIndexFiltersCategoryAndActiveState()
    {
        await using var db = await ContextAsync();
        db.Places.AddRange(
            NewPlace("Active vet", PlaceCategory.Veterinarian),
            NewPlace("Active shop", PlaceCategory.PetShop),
            new Place
            {
                Name = "Inactive vet", Category = PlaceCategory.Veterinarian,
                Latitude = 46.05, Longitude = 14.51, IsActive = false
            });
        await db.SaveChangesAsync();

        var admin = Admin(db);
        var active = Assert.IsType<ViewResult>(await admin.Index("active", null));
        Assert.Equal(2, Assert.IsAssignableFrom<IReadOnlyList<Place>>(active.Model).Count);
        var inactiveVets = Assert.IsType<ViewResult>(await admin.Index("inactive", PlaceCategory.Veterinarian));
        Assert.Equal("Inactive vet", Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Place>>(inactiveVets.Model)).Name);
    }

    [Fact]
    public async Task AdminLogoCreateReplaceRemove_LeavesPlacePhotoAlone()
    {
        await using var db = await ContextAsync();
        var logos = new FakeLogos();
        var admin = Admin(db, logos);
        var input = Input();
        input.ImageUrl = "https://example.com/photo.jpg";
        input.LogoFile = LogoFile();
        Assert.IsType<RedirectToActionResult>(await admin.Create(input));
        var place = await db.Places.SingleAsync();
        var original = place.LogoUrl;
        Assert.Equal(ManagedUrl('a'), original);
        Assert.Equal("https://example.com/photo.jpg", place.ImageUrl);

        var replacement = Input();
        replacement.ImageUrl = place.ImageUrl;
        replacement.LogoFile = LogoFile();
        Assert.IsType<RedirectToActionResult>(await admin.Edit(place.Id, replacement));
        Assert.Equal(ManagedUrl('b'), place.LogoUrl);
        Assert.Equal([original], logos.Deleted);

        var removal = Input();
        removal.ImageUrl = place.ImageUrl;
        removal.RemoveLogo = true;
        Assert.IsType<RedirectToActionResult>(await admin.Edit(place.Id, removal));
        Assert.Null(place.LogoUrl);
        Assert.Equal("https://example.com/photo.jpg", place.ImageUrl);
        Assert.Equal([original, ManagedUrl('b')], logos.Deleted);
    }

    [Fact]
    public async Task InvalidOrOversizedLogo_DoesNotCreatePlaceOrCallStorage()
    {
        await using var db = await ContextAsync();
        var logos = new FakeLogos();
        var invalid = Input();
        invalid.LogoFile = LogoFile([60, 115, 118, 103, 62], "logo.svg", "image/svg+xml");
        Assert.IsType<ViewResult>(await Admin(db, logos).Create(invalid));
        var oversized = Input();
        oversized.LogoFile = new FormFile(new MemoryStream([1]), 0, PlaceLogoUploadPolicy.MaxBytes + 1, "LogoFile", "logo.png")
            { Headers = new HeaderDictionary(), ContentType = "image/png" };
        Assert.IsType<ViewResult>(await Admin(db, logos).Create(oversized));
        Assert.Equal(0, logos.Uploads);
        Assert.Empty(await db.Places.ToListAsync());
    }

    [Fact]
    public async Task RemovingLegacyLogo_NeverDeletesExternalAsset()
    {
        await using var db = await ContextAsync();
        var place = NewPlace("Legacy", PlaceCategory.Veterinarian);
        place.LogoUrl = "https://example.com/old-logo.png";
        db.Places.Add(place);
        await db.SaveChangesAsync();
        var logos = new FakeLogos();
        Assert.IsType<RedirectToActionResult>(await Admin(db, logos).Edit(place.Id, new PlaceInput
        {
            Name = place.Name, Category = place.Category, Latitude = place.Latitude,
            Longitude = place.Longitude, RemoveLogo = true
        }));
        Assert.Null(place.LogoUrl);
        Assert.Empty(logos.Deleted);
    }

    [Fact]
    public async Task RemovingLogoStillReferencedByAnotherPlace_DoesNotDeleteAsset()
    {
        await using var db = await ContextAsync();
        var first = NewPlace("First", PlaceCategory.Veterinarian);
        var second = NewPlace("Second", PlaceCategory.PetShop);
        first.LogoUrl = second.LogoUrl = ManagedUrl('a');
        db.Places.AddRange(first, second);
        await db.SaveChangesAsync();
        var logos = new FakeLogos();
        var input = Input();
        input.RemoveLogo = true;
        Assert.IsType<RedirectToActionResult>(await Admin(db, logos).Edit(first.Id, input));
        Assert.Empty(logos.Deleted);
        Assert.Equal(ManagedUrl('a'), second.LogoUrl);
    }

    [Fact]
    public async Task FailedReplacementLeavesStoredPlaceAndOldLogoUntouched()
    {
        await using var db = await ContextAsync();
        var place = NewPlace("Original", PlaceCategory.Veterinarian);
        place.LogoUrl = ManagedUrl('a');
        db.Places.Add(place);
        await db.SaveChangesAsync();
        var changed = Input();
        changed.Name = "Changed";
        changed.LogoFile = LogoFile();
        var logos = new FakeLogos { FailUpload = true };
        Assert.IsType<ViewResult>(await Admin(db, logos).Edit(place.Id, changed));
        Assert.Equal("Original", (await db.Places.AsNoTracking().SingleAsync()).Name);
        Assert.Equal(ManagedUrl('a'), place.LogoUrl);
        Assert.Empty(logos.Deleted);
    }

    [Fact]
    public async Task MissingCloudinaryRejectsLogoButAllowsPlaceWithoutLogo()
    {
        await using var db = await ContextAsync();
        var withLogo = Input();
        withLogo.LogoFile = LogoFile();
        Assert.IsType<ViewResult>(await Admin(db).Create(withLogo));
        Assert.Empty(await db.Places.ToListAsync());
        Assert.IsType<RedirectToActionResult>(await Admin(db).Create(Input()));
        Assert.Null((await db.Places.SingleAsync()).LogoUrl);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task CreateSaveFailure_CleansOnlyWhenFreshDatabaseHasNoReference(bool commitBeforeThrow, bool shouldDelete)
    {
        await using var setup = await ContextAsync();
        await using var db = new ThrowingPlaceContext(Options(), commitBeforeThrow);
        var logos = new FakeLogos();
        var input = Input();
        input.LogoFile = LogoFile();
        Assert.IsType<ViewResult>(await Admin(db, logos).Create(input));
        Assert.Equal(shouldDelete, logos.Deleted.Contains(ManagedUrl('a')));
        Assert.Equal(commitBeforeThrow, await setup.Places.AsNoTracking().AnyAsync(place => place.LogoUrl == ManagedUrl('a')));
    }

    [Fact]
    public async Task CreateSaveFailure_ReferenceQueryFailureRetainsUploadedLogo()
    {
        await using var setup = await ContextAsync();
        await using var db = new ThrowingPlaceContext(Options(), false);
        var logos = new FakeLogos();
        var input = Input();
        input.LogoFile = LogoFile();
        Assert.IsType<ViewResult>(await Admin(db, logos, new FailingLogoReferenceReader()).Create(input));
        Assert.Empty(logos.Deleted);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task EditSaveFailure_PreservesOldLogoAndCleansNewOnlyWhenUnreferenced(bool commitBeforeThrow, bool shouldDeleteNew)
    {
        await using var setup = await ContextAsync();
        var place = NewPlace("Original", PlaceCategory.Veterinarian);
        place.LogoUrl = ManagedUrl('b');
        setup.Places.Add(place);
        await setup.SaveChangesAsync();
        await using var db = new ThrowingPlaceContext(Options(), commitBeforeThrow);
        var logos = new FakeLogos();
        var input = Input();
        input.LogoFile = LogoFile();
        Assert.IsType<ViewResult>(await Admin(db, logos).Edit(place.Id, input));
        var persisted = await setup.Places.AsNoTracking().SingleAsync();
        Assert.Equal(commitBeforeThrow ? ManagedUrl('a') : ManagedUrl('b'), persisted.LogoUrl);
        Assert.Equal(shouldDeleteNew, logos.Deleted.Contains(ManagedUrl('a')));
        Assert.DoesNotContain(ManagedUrl('b'), logos.Deleted);
    }

    [Fact]
    public async Task EditSaveFailure_ReferenceQueryFailureRetainsBothLogos()
    {
        await using var setup = await ContextAsync();
        var place = NewPlace("Original", PlaceCategory.Veterinarian);
        place.LogoUrl = ManagedUrl('b');
        setup.Places.Add(place);
        await setup.SaveChangesAsync();
        await using var db = new ThrowingPlaceContext(Options(), false);
        var logos = new FakeLogos();
        var input = Input();
        input.LogoFile = LogoFile();
        Assert.IsType<ViewResult>(await Admin(db, logos, new FailingLogoReferenceReader()).Edit(place.Id, input));
        Assert.Empty(logos.Deleted);
        Assert.Equal(ManagedUrl('b'), (await setup.Places.AsNoTracking().SingleAsync()).LogoUrl);
    }

    [Fact]
    public async Task InvalidEditPreservesStoredPlace_AndInvalidCreateAddsNothing()
    {
        await using var db = await ContextAsync();
        db.Places.Add(NewPlace("Original", PlaceCategory.Veterinarian));
        await db.SaveChangesAsync();
        var id = (await db.Places.SingleAsync()).Id;
        var admin = Admin(db);
        var invalid = Input((PlaceCategory)20);
        invalid.Name = "Changed";
        invalid.Latitude = double.NaN;
        invalid.WebsiteUrl = "javascript:alert(1)";

        Assert.IsType<ViewResult>(await admin.Edit(id, invalid));
        Assert.Equal("Original", (await db.Places.AsNoTracking().SingleAsync()).Name);
        Assert.IsType<ViewResult>(await Admin(db).Create(invalid));
        Assert.Equal(1, await db.Places.CountAsync());
    }

    [Fact]
    public async Task PublicDetailsExposeOnlyActivePlacesAndSafeLinks()
    {
        await using var db = await ContextAsync();
        var active = NewPlace("Clinic", PlaceCategory.Veterinarian);
        active.WebsiteUrl = "javascript:alert(1)";
        active.ImageUrl = "data:image/png,test";
        active.Phone = "+386 40 123 456";
        var inactive = NewPlace("Hidden", PlaceCategory.PetShop);
        inactive.IsActive = false;
        db.Places.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var controller = new PlacesController(db, new PlaceLogoCloudName("test"));
        var view = Assert.IsType<ViewResult>(await controller.Details(active.Id));
        var model = Assert.IsType<PlaceDetailsViewModel>(view.Model);
        Assert.Equal(PlaceCategory.Veterinarian, model.Category);
        Assert.Equal("Veterinar", model.CategoryLabel);
        Assert.Null(model.WebsiteUrl);
        Assert.Null(model.ImageUrl);
        Assert.Equal("tel:+38640123456", model.TelephoneHref);
        Assert.IsType<NotFoundResult>(await controller.Details(inactive.Id));
        Assert.IsType<NotFoundResult>(await controller.Details(-1));
    }

    [Fact]
    public async Task HomeMapProjectsOnlyActiveCompactPlacesForAnonymousZeroDogUser()
    {
        await using var db = await ContextAsync();
        var vet = NewPlace("Vet", PlaceCategory.Veterinarian);
        vet.LogoUrl = ManagedUrl('a');
        var shop = NewPlace("Shop", PlaceCategory.PetShop);
        shop.ImageUrl = "data:image/png,unsafe";
        db.Places.AddRange(vet, shop);
        var inactive = NewPlace("Inactive", PlaceCategory.Veterinarian);
        inactive.IsActive = false;
        inactive.ImageUrl = "https://example.com/inactive-logo.png";
        db.Places.Add(inactive);
        db.TrashBins.Add(new TrashBin { Name = "Bin", Latitude = 46.05, Longitude = 14.51, IsApproved = true });
        await db.SaveChangesAsync();

        var controller = new MapController(db, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            placeLogoCloud: new PlaceLogoCloudName("test"));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var view = Assert.IsType<ViewResult>(await controller.Index());
        var items = Assert.IsAssignableFrom<IReadOnlyList<PlaceMapItem>>((object)controller.ViewBag.ManagedPlaces);
        Assert.Equal(["Shop", "Vet"], items.Select(item => item.Name).OrderBy(name => name).ToArray());
        Assert.Equal([PlaceCategory.Veterinarian, PlaceCategory.PetShop], items.Select(item => item.Category).ToArray());
        Assert.All(items, item => Assert.True(item.Id > 0 && !string.IsNullOrWhiteSpace(item.Name)));
        Assert.Contains("c_fit,w_128,h_128", items.Single(item => item.Name == "Vet").LogoUrl);
        Assert.Null(items.Single(item => item.Name == "Shop").LogoUrl);
        var mapJson = JsonSerializer.Serialize(items, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"logoUrl\":\"https://res.cloudinary.com/test/image/upload/", mapJson);
        Assert.DoesNotContain("inactive-logo.png", mapJson);
        Assert.DoesNotContain("data:image", mapJson);
        Assert.Equal(7, typeof(PlaceMapItem).GetProperties().Length);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<TrashBin>>(view.Model));
    }

    private static PlaceInput Input(PlaceCategory category = PlaceCategory.Veterinarian) => new()
    {
        Name = "Test place", Category = category, Latitude = 46.05, Longitude = 14.51
    };

    private static Place NewPlace(string name, PlaceCategory category) => new()
    {
        Name = name, Category = category, Latitude = 46.05, Longitude = 14.51,
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
    };

    private DbContextOptions<ApplicationDbContext> Options() => new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={_db};Default Timeout=15;Pooling=False").Options;

    private async Task<ApplicationDbContext> ContextAsync()
    {
        var db = new ApplicationDbContext(Options());
        await db.Database.EnsureCreatedAsync();
        return db;
    }

    private static string ManagedUrl(char letter) => $"https://res.cloudinary.com/test/image/upload/v123/doggydrop/places/logos/{new string(letter, 32)}.webp";

    private static IFormFile LogoFile(byte[]? bytes = null, string name = "logo.png", string contentType = "image/png")
    {
        bytes ??= [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 0];
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "LogoFile", name)
            { Headers = new HeaderDictionary(), ContentType = contentType };
    }

    private sealed class FakeLogos : IPlaceLogoStorage
    {
        public int Uploads { get; private set; }
        public bool FailUpload { get; init; }
        public List<string?> Deleted { get; } = [];
        public Task<string?> UploadAsync(IFormFile file) => Task.FromResult<string?>(FailUpload ? null : ManagedUrl(++Uploads == 1 ? 'a' : 'b'));
        public Task DeleteManagedAsync(string? url) { Deleted.Add(url); return Task.CompletedTask; }
    }

    private AdminPlacesController Admin(ApplicationDbContext db, IPlaceLogoStorage? logos = null,
        IPlaceLogoReferenceReader? references = null)
    {
        var controller = new AdminPlacesController(db, logos ?? new MissingPlaceLogoStorage(),
            references ?? new PlaceLogoReferenceReader(Options()), new PlaceLogoCloudName("test"),
            NullLogger<AdminPlacesController>.Instance);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, "Admin")], "Test"))
            }
        };
        controller.TempData = new TempDataDictionary(controller.HttpContext, new MemoryTempDataProvider());
        return controller;
    }

    private sealed class FailingLogoReferenceReader : IPlaceLogoReferenceReader
    {
        public Task<bool> IsReferencedAsync(string logoUrl) => throw new InvalidOperationException("Simulated verification failure.");
    }

    private sealed class ThrowingPlaceContext(DbContextOptions<ApplicationDbContext> options, bool commitBeforeThrow)
        : ApplicationDbContext(options)
    {
        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            if (commitBeforeThrow) await base.SaveChangesAsync(cancellationToken);
            throw new DbUpdateException("Simulated ambiguous SaveChanges failure.");
        }
    }

    public void Dispose() { if (File.Exists(_db)) File.Delete(_db); }

    private sealed class MemoryTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
}
