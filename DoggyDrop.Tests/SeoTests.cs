using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class SeoTests
{
    [Theory]
    [InlineData("Č Š Ž", "c-s-z")]
    [InlineData("Pasji park – Slovenska Bistrica", "pasji-park-slovenska-bistrica")]
    [InlineData("  MR.PET   / \"Trgovina\" ", "mr-pet-trgovina")]
    [InlineData("Pes 🐕 in prijatelji", "pes-in-prijatelji")]
    [InlineData("///...'!?", "lokacija")]
    [InlineData("犬公園", "lokacija")]
    [InlineData("", "lokacija")]
    [InlineData(null, "lokacija")]
    public void SlugsAreSafeDeterministicAscii(string? name, string expected)
    {
        Assert.Equal(expected, SeoMetadata.Slug(name));
        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", SeoMetadata.Slug(name));
    }
    [Fact]
    public void SlugLengthIsBoundedAndIdsDisambiguateDuplicateNames()
    {
        Assert.InRange(SeoMetadata.Slug(new string('Ž', 500)).Length, 1, 80);
        Assert.NotEqual(SeoMetadata.PlacePath(1, "Park"), SeoMetadata.PlacePath(2, "Park"));
    }
    [Theory]
    [InlineData("http://doggydrop.app")]
    [InlineData("https://user:secret@doggydrop.app")]
    [InlineData("https://doggydrop.app/path")]
    [InlineData("https://doggydrop.app/?query=1")]
    [InlineData("https://doggydrop.app/#fragment")]
    [InlineData("https://127.0.0.1")]
    [InlineData("//evil.example")]
    public void InvalidPublicOriginFailsClosed(string origin) => Assert.Throws<InvalidOperationException>(() => Site(origin));

    internal static SeoSite Site(string origin = "https://doggydrop.app", string environment = "Production", bool enabled = true, bool preview = false) =>
        new(Config(origin, enabled, preview), new TestEnvironment { EnvironmentName = environment });
    internal static IConfiguration Config(string origin = "https://doggydrop.app", bool enabled = true, bool preview = false) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
            ["Seo:PublicOrigin"] = origin, ["Seo:AllowIndexing"] = enabled.ToString(), ["IS_PULL_REQUEST"] = preview.ToString()
        }).Build();
    internal sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Production";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
    [Theory]
    [InlineData("Production", true, false, "doggydrop.app", true)]
    [InlineData("Production", true, false, "doggydrop.app:443", true)]
    [InlineData("Production", true, false, "evil.example", false)]
    [InlineData("Production", true, false, "preview.onrender.com", false)]
    [InlineData("Production", true, true, "doggydrop.app", false)]
    [InlineData("Production", false, false, "doggydrop.app", false)]
    [InlineData("Development", true, false, "doggydrop.app", false)]
    [InlineData("Staging", true, false, "doggydrop.app", false)]
    public void IndexingRequiresProductionCanonicalHostAndNonPreview(string environment, bool enabled, bool preview, string host, bool expected)
    {
        var context = new DefaultHttpContext(); context.Request.Host = new(host);
        Assert.Equal(expected, Site(environment: environment, enabled: enabled, preview: preview).AllowsIndexing(context));
    }
    [Fact]
    public void IncomingHostCannotBeMadeIndexableByForwardedHost()
    {
        var context = new DefaultHttpContext(); context.Items[SeoMetadata.RawHostKey] = "preview.onrender.com";
        context.Request.Host = new("doggydrop.app");
        Assert.False(Site().AllowsIndexing(context));
        Assert.Equal("https://doggydrop.app/Places", Site().Absolute("/Places"));
        Assert.Throws<ArgumentException>(() => Site().Absolute("//evil.example"));
    }
    [Theory]
    [InlineData(PlaceCategory.Veterinarian, "VeterinaryCare")]
    [InlineData(PlaceCategory.PetShop, "PetStore")]
    [InlineData(PlaceCategory.Groomer, "LocalBusiness")]
    [InlineData(PlaceCategory.DogSchool, "LocalBusiness")]
    [InlineData(PlaceCategory.DogFriendlyCafe, "CafeOrCoffeeShop")]
    [InlineData(PlaceCategory.DogPark, "Park")]
    [InlineData(PlaceCategory.DogBeach, "Place")]
    public void JsonLdIsFactualTypedAndSafe(PlaceCategory category, string type)
    {
        var place = PlaceDetailsViewModel.FromPublicData(new(42, "Čuvaj </script><script>alert(1)</script>", category, 46.1, 15.2,
            "Ulica \"A\" <b>1</b>", "+386 123456", null, "Never used hours", "Never used copy", null, null, []), "test");
        var seo = SeoMetadata.ForPlace(place);
        var json = seo.JsonLd(Site()); Assert.DoesNotContain("</script", json);
        using var parsed = JsonDocument.Parse(json); var root = parsed.RootElement;
        Assert.Equal(type, root.GetProperty("@type").GetString());
        Assert.Equal(place.Name, root.GetProperty("name").GetString());
        Assert.Equal(place.Address, root.GetProperty("address").GetString());
        var geo = type == "VeterinaryCare" ? root.GetProperty("location").GetProperty("geo") : root.GetProperty("geo");
        Assert.Equal(46.1, geo.GetProperty("latitude").GetDouble());
        foreach (var absent in new[] { "aggregateRating", "review", "openingHours", "priceRange", "award", "sponsor", "AmenitiesSourceUrl", "AmenitiesVerifiedAt", "DataSource", "IsSaved", "Featured" })
            Assert.DoesNotContain(absent, json);
        Assert.Equal(json, SeoMetadata.ForPlace(place with { IsSaved = true, IsCurrentlyFeatured = true }).JsonLd(Site()));
        Assert.Equal(SeoMetadata.ForPlace(place).CanonicalPath, SeoMetadata.ForPlace(place with { IsCurrentlyFeatured = true }).CanonicalPath);
    }
    [Fact]
    public void UnverifiedExternalPhotosFallBackToExistingBrandAsset()
    {
        var place = PlaceDetailsViewModel.FromPublicData(new(1,"Park",PlaceCategory.DogPark,46,15,null,null,null,null,null,"https://broken.example/photo.png",null,[]),"test");
        Assert.Equal("https://doggydrop.app/images/icon-512.png", SeoMetadata.ForPlace(place).SocialImage(Site()));
        Assert.DoesNotContain("broken.example", SeoMetadata.ForPlace(place).JsonLd(Site()));
    }
    [Fact]
    public async Task SitemapUsesOneMinimalQueryAndExcludesInvalidOrPrivateRecords()
    {
        var sql = new Queries();
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite("Data Source=:memory:").AddInterceptors(sql).Options);
        await db.Database.OpenConnectionAsync(); await db.Database.EnsureCreatedAsync();
        db.Places.AddRange(
            new Place {Id=1,Name="Čuvaj & Park",Category=PlaceCategory.DogPark,Latitude=46,Longitude=15},
            new Place {Id=2,Name="Hidden",Category=PlaceCategory.PetShop,IsActive=false},
            new Place {Id=3,Name="Unsupported",Category=(PlaceCategory)99},
            new Place {Id=4,Name=" ",Category=PlaceCategory.DogPark},
            new Place {Id=5,Name="Invalid latitude",Category=PlaceCategory.DogPark,Latitude=91},
            new Place {Id=6,Name="Invalid longitude",Category=PlaceCategory.DogPark,Longitude=double.PositiveInfinity},
            new Place {Id=7,Name="Control\u0001",Category=PlaceCategory.DogPark},
            new Place {Id=8,Name="\u2003",Category=PlaceCategory.DogPark},
            new Place {Id=9,Name=new string('x',121),Category=PlaceCategory.DogPark});
        await db.SaveChangesAsync(); db.ChangeTracker.Clear(); sql.Commands.Clear();
        var controller = new SeoController(db, Config(), new TestEnvironment()) {
            ControllerContext = new() { HttpContext = new DefaultHttpContext() }
        };
        controller.Request.Host = new("doggydrop.app");
        var content = Assert.IsType<ContentResult>(await controller.Sitemap(default));
        Assert.StartsWith("application/xml", content.ContentType);
        var xml = XDocument.Parse(content.Content!); XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        Assert.Equal(new[]{"https://doggydrop.app/","https://doggydrop.app/Places","https://doggydrop.app/projekt","https://doggydrop.app/obcine","https://doggydrop.app/lokacije/1/cuvaj-park"}, xml.Descendants(ns+"loc").Select(e=>e.Value));
        Assert.Single(xml.Descendants(ns+"lastmod")); Assert.Empty(db.ChangeTracker.Entries());
        var query = Assert.Single(sql.Commands);
        var projection = query[..query.IndexOf("FROM", StringComparison.Ordinal)];
        foreach(var excluded in new[]{"Amenit","Saved","DataSource","Logo","Featured","Latitude","Longitude"}) Assert.DoesNotContain(excluded, projection);
        var places = new PlacesController(db,new PlaceLogoCloudName("test"));
        foreach(var id in Enumerable.Range(2,8)) Assert.IsType<NotFoundResult>(await places.Details(id));
    }
    private sealed class Queries : DbCommandInterceptor
    {
        public List<string> Commands {get;}=[];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData e, InterceptionResult<DbDataReader> r, CancellationToken ct=default)
        {Commands.Add(command.CommandText);return ValueTask.FromResult(r);}
    }
}
