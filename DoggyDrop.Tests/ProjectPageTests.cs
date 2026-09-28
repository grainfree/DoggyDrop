using System.Data.Common;
using System.Net;
using System.Text.RegularExpressions;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace DoggyDrop.Tests;

// Use the existing isolated MVC host, including the real compiled Razor views.
public sealed partial class SeoHttpTests
{
    private readonly ProjectConnectionGuard projectConnectionGuard = new();

    [Theory]
    [InlineData("/projekt", "O projektu DoggyDrop")]
    [InlineData("/obcine", "DoggyDrop za občine")]
    public async Task ProjectPagesAreAnonymousWithSharedSeoAndSemanticNavigation(string path, string title)
    {
        using var client = Client();
        var response = await client.GetAsync(path + "?utm_source=outreach");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Robots(response, "index, follow");
        var html = await response.Content.ReadAsStringAsync();
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains($"<title>{title}</title>", decoded);
        Assert.Single(Regex.Matches(html, "<title>"));
        Assert.Single(Regex.Matches(html, "name=\"description\""));
        Assert.Single(Regex.Matches(html, "name=\"robots\" content=\"index, follow\""));
        Assert.Equal("https://doggydrop.app" + path, Canonical(html));
        Assert.Contains($"property=\"og:title\" content=\"{title}\"", decoded);
        Assert.Contains($"property=\"og:url\" content=\"https://doggydrop.app{path}\"", html);
        Assert.Contains("property=\"og:description\"", html);
        Assert.Contains("property=\"og:image\" content=\"https://doggydrop.app/images/icon-512.png\"", html);
        Assert.Single(Regex.Matches(html, "<h1\\b"));
        Assert.Single(Regex.Matches(html, "<main\\b"));
        Assert.Single(Regex.Matches(html, "<footer\\b"));
        Assert.Equal(2, Regex.Matches(html, "<nav\\b").Count);
        foreach (var href in new[] { "/projekt", "/obcine", "/", "/Home/Privacy", "/Home/Terms", "#kontakt", "#vsebina" })
            Assert.Contains($"href=\"{href}\"", html);
        Assert.Contains("id=\"kontakt\"", html);
        Assert.Contains("<html lang=\"sl\">", html);
        Assert.Contains("href=\"/manifest.json\"", html);
        foreach (var absent in new[] { "app-bottom-nav", "pwaPrompt", "home-intro.js", "<script", "Prijava", "IsSaved" })
            Assert.DoesNotContain(absent, html);
        Assert.Empty(Json(html)); // No fabricated Organization or unnecessary structured data.
        Assert.True(response.Headers.CacheControl!.NoStore);
        await Capture(path == "/projekt" ? "project-page" : "municipal-page", html);
    }

    [Theory]
    [InlineData("/projekt", false)]
    [InlineData("/obcine", false)]
    [InlineData("/projekt", true)]
    [InlineData("/obcine", true)]
    public async Task InformationalPagesNeverOpenDatabaseEvenForSignedInVisitors(string path, bool signedIn)
    {
        projectConnectionGuard.RejectConnections = true;
        using var client = Client(signedIn: signedIn);
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Robots(response, signedIn ? "noindex, nofollow" : "index, follow");
        Assert.DoesNotContain("Alice", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/projekt", "Production", true, true, "doggydrop.app")]
    [InlineData("/obcine", "Production", true, true, "doggydrop.app")]
    [InlineData("/projekt", "Staging", false, true, "doggydrop.app")]
    [InlineData("/obcine", "Staging", false, true, "doggydrop.app")]
    [InlineData("/projekt", "Production", false, false, "doggydrop.app")]
    [InlineData("/obcine", "Production", false, false, "doggydrop.app")]
    [InlineData("/projekt", "Production", false, true, "evil.example")]
    [InlineData("/obcine", "Production", false, true, "evil.example")]
    public async Task ProjectPagesRespectEnvironmentAndHostIndexingGuards(string path, string environment, bool preview, bool enabled, string host)
    {
        app.Environment.EnvironmentName = environment;
        app.Services.GetRequiredService<IHostEnvironment>().EnvironmentName = environment;
        app.Configuration["IS_PULL_REQUEST"] = preview.ToString();
        app.Configuration["Seo:AllowIndexing"] = enabled.ToString();
        using var client = Client(host);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "doggydrop.app");
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Robots(response, "noindex, nofollow");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("name=\"robots\" content=\"noindex, nofollow\"", html);
        Assert.Equal("https://doggydrop.app" + path, Canonical(html));
    }

    [Fact]
    public async Task ProjectContentExplainsContributionAndAllSevenCategoriesWithoutClaims()
    {
        using var client = Client();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/projekt"));
        foreach (var category in PlaceCategories.All) Assert.Contains($"<li>{category.FilterLabel}</li>", html);
        foreach (var href in new[] { "/Map/Add", "/Places", "#sodelovanje" }) Assert.Contains($"href=\"{href}\"", html);
        Assert.Contains("prijava ni potrebna", html);
        Assert.Contains("javnih in občinskih podatkov", html);
        Assert.Contains("urejanja skrbnikov", html);
        Assert.Contains("neodvisen projekt", html);
        Assert.DoesNotMatch("(?i)\\b(največji|najboljši|vodilni|preverjeno|uradno|zagotavlja|dokazano)\\b|vsa Slovenija|vsi koši", html);
    }

    [Fact]
    public async Task MunicipalContentExplainsRequestCostFormatsAndSeparationFromUserData()
    {
        using var client = Client();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/obcine"));
        foreach (var concept in new[] { "neodvisna", "komunalna podjetja", "GPS-koordinate", "Excelu", "CSV-ju", "GIS", "brezplačna", "pogoje njihove uporabe", "interno zabeleži vir", "sprehodi uporabnikov so ločeni", "sledenje uporabnikom" })
            Assert.Contains(concept, html);
        Assert.DoesNotMatch("(?i)\\b(največji|najboljši|vodilni|preverjeno|uradno|zagotavlja|dokazano)\\b|vsa Slovenija|vsi koši", html);
        Assert.DoesNotContain("private.example", html);
        Assert.DoesNotContain("AmenitiesSourceUrl", html);
    }

    [Theory]
    [InlineData("/projekt")]
    [InlineData("/obcine")]
    public async Task PublicContactIsConfiguredEncodedAndSafeForMailto(string path)
    {
        app.Configuration["DoggyDrop:ContactEmail"] = "dogs&friends@example.org";
        using var client = Client();
        var html = await client.GetStringAsync(path);
        Assert.Contains("dogs&amp;friends@example.org", html);
        Assert.Contains("href=\"mailto:dogs%26friends@example.org\"", html);
        Assert.DoesNotContain("admin@doggydrop.app", html);
    }

    [Theory]
    [InlineData("/projekt", null)]
    [InlineData("/obcine", "")]
    [InlineData("/projekt", "admin@example.org\r\nBcc:private@example.org")]
    [InlineData("/obcine", "\"><script>alert(1)</script>@example.org")]
    [InlineData("/obcine", "admin@example.org?subject=bad&bcc=private@example.org")]
    [MemberData(nameof(MalformedConfiguredContacts))]
    public async Task MissingOrUnsafeContactRendersGracefullyWithoutLeakingValue(string path, string? email)
    {
        app.Configuration["DoggyDrop:ContactEmail"] = email;
        using var client = Client();
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Kontaktni e-poštni naslov trenutno ni na voljo.", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("mailto:", html);
        Assert.DoesNotContain("private@example.org", html);
        Assert.DoesNotContain("<script", html);
    }

    public static IEnumerable<object[]> MalformedConfiguredContacts()
    {
        foreach (var path in new[] { "/projekt", "/obcine" })
            foreach (var mailbox in PublicContactTests.InvalidLocalPartsAndDomains())
                yield return new object[] { path, mailbox[0] };
    }

    private sealed class ProjectConnectionGuard : DbConnectionInterceptor
    {
        public bool RejectConnections { get; set; }
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            if (RejectConnections) throw new InvalidOperationException("Informational page attempted database access.");
            return result;
        }
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ConnectionOpening(connection, eventData, result));
    }
}

public sealed class PublicContactTests
{
    public static IEnumerable<object[]> InvalidLocalPartsAndDomains()
    {
        foreach (var email in new[] { "a..b@example.org", "a.@example.org", ".a@example.org",
            new string('a', 65) + "@example.org", "admin@example..org" })
            yield return new object[] { email };
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("Name <admin@example.org>")]
    [InlineData("admin@example.org\r\nBcc:other@example.org")]
    [InlineData("admin@example.org?subject=test")]
    [InlineData("admin@example.org#fragment")]
    [InlineData("admin@localhost")]
    [InlineData("admin@example.org,other@example.org")]
    [MemberData(nameof(InvalidLocalPartsAndDomains))]
    public void InvalidPublicEmailIsOmitted(string? email) => Assert.Null(PublicContact.Parse(email));

    [Fact]
    public void SixtyFourAsciiCharactersAreAllowedInTheLocalPart()
    {
        var email = new string('a', 64) + "@example.org";
        var contact = Assert.IsType<PublicContact>(PublicContact.Parse(email));
        Assert.Equal(email, contact.Email);
        Assert.Equal("mailto:" + email, contact.Mailto);
    }

    [Theory]
    [InlineData("admin@doggydrop.app", "mailto:admin@doggydrop.app")]
    [InlineData("dogs+partners@example.org", "mailto:dogs%2Bpartners@example.org")]
    [InlineData("dogs&friends@example.org", "mailto:dogs%26friends@example.org")]
    public void PublicEmailHasOnlyAnEncodedRecipient(string email, string href)
    {
        var contact = Assert.IsType<PublicContact>(PublicContact.Parse(email));
        Assert.Equal(email, contact.Email);
        Assert.Equal(href, contact.Mailto);
    }
}
