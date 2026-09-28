using System.Data.Common;
using System.Security.Claims;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Migrations;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class SavedPlacesTests : IDisposable
{
    private readonly string database = Path.Combine(Path.GetTempPath(), $"doggydrop-saved-{Guid.NewGuid():N}.db");
    private readonly QueryCounter queries = new();

    private ApplicationDbContext Context() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={database};Pooling=False").AddInterceptors(queries).Options);

    private async Task<ApplicationDbContext> Seed()
    {
        var db = Context();
        await db.Database.EnsureCreatedAsync();
        db.Users.AddRange(new ApplicationUser { Id = "alice", UserName = "alice" },
            new ApplicationUser { Id = "bob", UserName = "bob" });
        foreach (var category in PlaceCategories.Supported)
            db.Places.Add(new Place { Name = $"Place {category}", Category = category, Latitude = 46, Longitude = 15,
                Address = "Test street", LogoUrl = $"https://res.cloudinary.com/test/image/upload/v123/doggydrop/places/logos/{new string('a', 32)}.webp" });
        await db.SaveChangesAsync();
        queries.Reads.Clear();
        return db;
    }

    private static T Setup<T>(T controller, string? user = "alice", bool authenticated = true) where T : Controller
    {
        var claims = user == null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.NameIdentifier, user)];
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticated ? "Test" : null)) };
        controller.ControllerContext = new ControllerContext { HttpContext = http, RouteData = new Microsoft.AspNetCore.Routing.RouteData() };
        controller.TempData = new TempDataDictionary(http, new MemoryTempData());
        controller.Url = new UrlHelper(controller.ControllerContext);
        return controller;
    }

    private static SavedPlacesController Saved(ApplicationDbContext db, string? user = "alice", bool authenticated = true) =>
        Setup(new SavedPlacesController(db, new PlaceLogoCloudName("test")), user, authenticated);

    private static PlacesController Public(ApplicationDbContext db, string? user = "alice", bool authenticated = true) =>
        Setup(new PlacesController(db, new PlaceLogoCloudName("test")), user, authenticated);

    private static PlaceDiscoveryViewModel List(IActionResult result) =>
        Assert.IsType<PlaceDiscoveryViewModel>(Assert.IsType<ViewResult>(result).Model);

    [Fact]
    public async Task SaveAndRemoveAreIdempotentAndPreserveOriginalSavedAt()
    {
        await using var db = await Seed();
        var controller = Saved(db);
        Assert.IsType<LocalRedirectResult>(await controller.Save(1, "/Places/Details/1"));
        var first = await db.SavedPlaces.AsNoTracking().SingleAsync();
        Assert.Equal("alice", first.UserId);
        Assert.Equal(1, first.PlaceId);
        Assert.InRange(first.SavedAt, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(1));
        await controller.Save(1, "/Places");
        Assert.Equal(first.SavedAt, (await db.SavedPlaces.AsNoTracking().SingleAsync()).SavedAt);
        Assert.Single(List(await controller.Index()).Places);
        await controller.Remove(1, "/SavedPlaces");
        await controller.Remove(1, "/SavedPlaces");
        Assert.Empty(await db.SavedPlaces.ToListAsync());
        Assert.Empty(List(await controller.Index()).Places);
        Assert.Equal(7, await db.Places.CountAsync());
    }

    [Fact]
    public async Task TwoUsersHavePrivateIndependentRelationsAndCannotRemoveEachOthersSave()
    {
        await using var db = await Seed();
        await Saved(db, "alice").Save(1, null);
        await Saved(db, "bob").Save(2, null);
        Assert.Equal(1, Assert.Single(List(await Saved(db, "alice").Index()).Places).Id);
        Assert.Equal(2, Assert.Single(List(await Saved(db, "bob").Index()).Places).Id);
        await Saved(db, "bob").Remove(1, null);
        Assert.True(await db.SavedPlaces.AnyAsync(s => s.UserId == "alice" && s.PlaceId == 1));
        await Saved(db, "bob").Save(1, null);
        Assert.Equal(2, await db.SavedPlaces.CountAsync(s => s.PlaceId == 1));
        await Saved(db, "alice").Remove(1, null);
        Assert.True(await db.SavedPlaces.AnyAsync(s => s.UserId == "bob" && s.PlaceId == 1));
    }

    [Fact]
    public async Task InactiveAndUnsupportedPlacesStayHiddenAndReactivationRestoresSavedState()
    {
        await using var db = await Seed();
        await Saved(db).Save(1, null);
        var place = await db.Places.SingleAsync(p => p.Id == 1);
        place.IsActive = false;
        await db.SaveChangesAsync();
        Assert.Empty(List(await Saved(db).Index()).Places);
        Assert.IsType<NotFoundResult>(await Public(db).Details(1));
        Assert.IsType<NotFoundResult>(await Saved(db).Save(1, null));
        Assert.Single(await db.SavedPlaces.ToListAsync());
        place.IsActive = true;
        await db.SaveChangesAsync();
        Assert.Single(List(await Saved(db).Index()).Places);
        Assert.True(Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await Public(db).Details(1)).Model).IsSaved);
        place.Category = (PlaceCategory)99;
        await db.SaveChangesAsync();
        Assert.Empty(List(await Saved(db).Index()).Places);
        Assert.DoesNotContain(List(await Public(db).Index()).Places, p => p.Id == 1);
        Assert.IsType<NotFoundResult>(await Public(db).Details(1));
        Assert.IsType<NotFoundResult>(await Saved(db).Save(1, null));
        Assert.IsType<NotFoundResult>(await Saved(db).Save(99999, null));
        Assert.Single(await db.SavedPlaces.ToListAsync());
        await Saved(db).Remove(1, null);
        Assert.Empty(await db.SavedPlaces.ToListAsync());
    }

    [Fact]
    public async Task SavedPageUsesAllSevenCategoriesSharedLogoRulesAndNewestFirst()
    {
        await using var db = await Seed();
        var places = await db.Places.OrderBy(p => p.Id).ToListAsync();
        foreach (var place in places)
        {
            await Saved(db).Save(place.Id, null);
            await db.SavedPlaces.Where(s => s.UserId == "alice" && s.PlaceId == place.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.SavedAt, DateTime.UtcNow.Date.AddMinutes(place.Id)));
        }
        queries.Reads.Clear();
        var saved = List(await Saved(db).Index());
        Assert.Single(queries.Reads);
        Assert.Equal(places.Select(p => p.Id).Reverse(), saved.Places.Select(p => p.Id));
        Assert.Equal(7, saved.SavedPlaceIds.Count);
        foreach (var item in saved.Places)
        {
            var category = PlaceCategories.Get(item.Category);
            Assert.Equal(category.IconClass, item.IconClass);
            Assert.Equal(category.Label, item.CategoryLabel);
            if (category.IsCommercial) Assert.Contains("c_fit,w_128,h_128", item.LogoUrl);
            else Assert.Null(item.LogoUrl);
        }
        places[0].LogoUrl = "https://unmanaged.example/logo.png";
        await db.SaveChangesAsync();
        Assert.Null(List(await Saved(db).Index()).Places.Single(p => p.Id == places[0].Id).LogoUrl);
    }

    [Fact]
    public async Task DiscoveryAndDetailsQueryOnlyCurrentUsersStateAndSkipItForAnonymous()
    {
        await using var db = await Seed();
        await Saved(db).Save(1, null);
        await Saved(db, "bob").Save(2, null);
        queries.Reads.Clear();
        var discovery = List(await Public(db).Index());
        Assert.Equal(7, discovery.Places.Count);
        Assert.Equal([1], discovery.SavedPlaceIds);
        Assert.Equal(2, queries.Reads.Count);
        Assert.Single(queries.Reads, sql => sql.Contains("SavedPlaces"));
        Assert.True(Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await Public(db).Details(1)).Model).IsSaved);
        Assert.False(Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await Public(db).Details(2)).Model).IsSaved);
        queries.Reads.Clear();
        Assert.Empty(List(await Public(db, null, false).Index()).SavedPlaceIds);
        Assert.False(Assert.IsType<PlaceDetailsViewModel>(Assert.IsType<ViewResult>(await Public(db, null, false).Details(1)).Model).IsSaved);
        Assert.Equal(2, queries.Reads.Count);
        Assert.DoesNotContain(queries.Reads, sql => sql.Contains("SavedPlaces"));
        Assert.Null(typeof(PlaceDiscoveryItem).GetProperty("SavedPlace"));
        Assert.Null(typeof(PlaceMapItem).GetProperty("UserId"));
        Assert.Equal(13, typeof(PlaceMapItem).GetProperties().Length);
        Assert.NotNull(typeof(PlaceMapItem).GetProperty("DetailsUrl"));
        Assert.NotNull(typeof(PlaceMapItem).GetProperty("IsCurrentlyFeatured"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("alice", false)]
    [InlineData(null, true)]
    public async Task AnonymousOrMissingIdentityCannotReadOrMutate(string? user, bool authenticated)
    {
        await using var db = await Seed();
        var controller = Saved(db, user, authenticated);
        Assert.IsType<ChallengeResult>(await controller.Index());
        Assert.IsType<ChallengeResult>(await controller.Save(1, null));
        Assert.IsType<ChallengeResult>(await controller.Remove(1, null));
        Assert.Empty(await db.SavedPlaces.ToListAsync());
    }

    [Fact]
    public void ActionsRequireAuthenticationAndAntiforgeryAndNeverBindUserId()
    {
        Assert.Single(typeof(SavedPlacesController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        var cache = Assert.IsType<ResponseCacheAttribute>(Assert.Single(typeof(SavedPlacesController).GetCustomAttributes(typeof(ResponseCacheAttribute), true)));
        Assert.True(cache.NoStore);
        foreach (var name in new[] { "Save", "Remove" })
        {
            var action = typeof(SavedPlacesController).GetMethod(name)!;
            Assert.Single(action.GetCustomAttributes(typeof(HttpPostAttribute), true));
            Assert.Single(action.GetCustomAttributes(typeof(ValidateAntiForgeryTokenAttribute), true));
            Assert.Empty(action.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
            Assert.Equal(["placeId", "returnUrl"], action.GetParameters().Select(p => p.Name));
        }
        foreach (var name in new[] { "Index", "Details" })
            Assert.Single(typeof(PlacesController).GetMethod(name)!.GetCustomAttributes(typeof(AllowAnonymousAttribute), true));
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("//evil.example")]
    [InlineData("/\\evil.example")]
    [InlineData("javascript:alert(1)")]
    [InlineData(null)]
    public async Task UnsafeReturnUrlsFallBackToSavedPage(string? returnUrl)
    {
        await using var db = await Seed();
        var controller = Saved(db);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await controller.Save(1, returnUrl)).ActionName);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await controller.Remove(1, returnUrl)).ActionName);
    }

    [Fact]
    public async Task ConcurrentRequestsWithSeparateContextsAreIdempotent()
    {
        await using var db = await Seed();
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var requestDb = Context();
            Assert.IsType<RedirectToActionResult>(await Saved(requestDb).Save(1, null));
        })));
        Assert.Single(await db.SavedPlaces.ToListAsync());
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var requestDb = Context();
            Assert.IsType<RedirectToActionResult>(await Saved(requestDb).Remove(1, null));
        })));
        Assert.Empty(await db.SavedPlaces.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task ForeignKeysCascadeOnlyRelationsAndCompositeKeyPreventsDuplicates()
    {
        await using var db = await Seed();
        await Saved(db).Save(1, null);
        await Saved(db, "bob").Save(1, null);
        await Saved(db).Save(2, null);
        await db.Places.Where(p => p.Id == 1).ExecuteDeleteAsync();
        Assert.Equal(2, (await db.SavedPlaces.SingleAsync()).PlaceId);
        await db.Users.Where(u => u.Id == "alice").ExecuteDeleteAsync();
        Assert.Empty(await db.SavedPlaces.ToListAsync());
        Assert.Equal(6, await db.Places.CountAsync());
        var entity = db.Model.FindEntityType(typeof(SavedPlace))!;
        Assert.Equal(["UserId", "PlaceId"], entity.FindPrimaryKey()!.Properties.Select(p => p.Name));
        Assert.All(entity.GetForeignKeys(), fk => Assert.Equal(DeleteBehavior.Cascade, fk.DeleteBehavior));
        db.SavedPlaces.Add(new SavedPlace { UserId = "bob", PlaceId = 2 });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        db.SavedPlaces.Add(new SavedPlace { UserId = "bob", PlaceId = 2 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlaceChangedBetweenValidationAndInsertDoesNotReportFalseSuccess(bool unsupported)
    {
        await using var db = await Seed();
        queries.BeforeInsert = command =>
        {
            using var change = command.Connection!.CreateCommand();
            change.CommandText = unsupported
                ? "UPDATE Places SET Category = 99 WHERE Id = 1"
                : "UPDATE Places SET IsActive = 0 WHERE Id = 1";
            change.ExecuteNonQuery();
        };
        var controller = Saved(db);
        Assert.IsType<NotFoundResult>(await controller.Save(1, null));
        Assert.Empty(await db.SavedPlaces.ToListAsync());
        Assert.False(controller.TempData.ContainsKey("SavedPlaceMessage"));
    }

    [Fact]
    public void MigrationAddsOnlySavedPlacesAndDownDropsOnlyThatTable()
    {
        var migration = new AddSavedPlaces();
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("SavedPlaces", table.Name);
        Assert.Equal(["UserId", "PlaceId", "SavedAt"], table.Columns.Select(c => c.Name));
        Assert.Equal(["UserId", "PlaceId"], table.PrimaryKey!.Columns);
        Assert.Equal(2, table.ForeignKeys.Count);
        Assert.All(table.ForeignKeys, fk => Assert.Equal(ReferentialAction.Cascade, fk.OnDelete));
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), index => Assert.Equal("SavedPlaces", index.Table));
        Assert.Equal("SavedPlaces", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
    }

    public void Dispose() { if (File.Exists(database)) File.Delete(database); }

    private sealed class MemoryTempData : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    private sealed class QueryCounter : DbCommandInterceptor
    {
        public List<string> Reads { get; } = [];
        public Action<DbCommand>? BeforeInsert { get; set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("INSERT INTO \"SavedPlaces\"", StringComparison.Ordinal) && BeforeInsert != null)
            {
                var action = BeforeInsert;
                BeforeInsert = null;
                action(command);
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            lock (Reads) Reads.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
