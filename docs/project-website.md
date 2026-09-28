# Epic 22 — Project website and municipal outreach

Started from clean `main` at `26c594617673a093e6394eb4411cec7c0597b835` (Epic 21). No migration, manual deployment or production access.

## Pages and boundaries

- `/projekt`: Slovenian introduction, four practical benefits, restrained environmental message, community contribution, the existing seven Place categories, municipal cooperation and contact.
- `/obcine`: introduction for municipalities/utility companies, requested location data, flexible Excel/CSV/GIS/public-source guidance, free inclusion, coordination before reuse, internal provenance and separation from user GPS/walk data.
- No `/sodeluj`: participation fits in the project's `#sodelovanje` section and shared contact section without another redundant page.
- Map links use the existing Map/Index action (currently `/`); public bin suggestions use Map/Add, and Discovery uses Places/Index. Root, Google login, app navigation, first-run behavior and PWA start URL/scope remain unchanged.
- One small link from the existing About page provides a return path. Home map controls are unchanged.

The dedicated `_ProjectLayout` uses existing colors, typography and brand images with a small CSS file (~10 KiB). It has public navigation, a skip link, one main landmark and a footer. There is no app bottom bar, install interruption, JavaScript, analytics, map library, live counter or external data/API request on these pages. The existing Google Fonts family is reused, with system-font fallbacks. The map-like visual is an HTML/CSS/SVG illustration labelled as such, not a screenshot or real location dataset. No stock images or new dependencies.

The controller and layout have no database dependency or personal content. Tests reject database connections while rendering both anonymous and signed-in requests. Existing authentication middleware continues to operate normally; no authentication configuration changed. Informational responses are no-store, preserving conservative cache behavior.

## Contact

`DoggyDrop:ContactEmail` is public configuration. Its default is `admin@doggydrop.app`, already explicitly published as DoggyDrop's contact in `Views/Home/About.cshtml` before this Epic. No personal address or SMTP/source-contact configuration is used. An environment override can use `DoggyDrop__ContactEmail`.

`PublicContact.Parse` accepts a single conventional ASCII mailbox and rejects control characters (including CR/LF), display-name syntax, query/fragment payloads and invalid addresses. The local part is limited to 64 ASCII characters/octets and cannot start or end with a dot or contain consecutive dots. Razor encodes visible text; the mailto recipient's local part is URI-escaped. No subject/body/header parameters are generated. Empty/invalid configuration displays an honest unavailable-contact message and a working map link, without leaking the rejected value or creating a broken mailto.

Mailbox delivery/monitoring was not tested. The existing Help page mentions a different address (`info@doggydrop.app`); this Epic deliberately uses the explicit public About-page contact rather than silently changing those unrelated pages.

## SEO and future routing

The existing Epic 21 head markup is extracted into `_SeoHead` and reused by both layouts. The same configured canonical origin, OG image, noindex environment policy and script-safe Place JSON-LD behavior remain. The informational pages have factual titles/descriptions and clean canonicals, with no invented Organization details or unnecessary new structured-data type.

Both pages join the existing sitemap as static URLs without fabricated lastmod. Home appears once. Place eligibility/projection and robots behavior are unchanged. Production anonymous requests are indexable; preview/Staging/opt-out requests remain noindex. Authenticated requests retain Epic 21's noindex policy without personalized informational content.

Route paths are constants in the existing SEO metadata class and are used by route attributes/sitemap; internal links use MVC route generation. Views are separate from application Home. Later content/root changes do not require rewriting presentation content, and canonical-domain changes remain centralized in `Seo:PublicOrigin`. No domain split is performed.

## Copy and privacy boundaries

Copy makes no claims of government affiliation, comprehensive coverage, verified field visits, proven environmental outcomes, savings, adoption or Featured entitlement. Municipal inclusion is explicitly free. Reuse conditions are discussed rather than inventing a license or promising a public attribution format. Internal provenance is explained without loading/exposing source contacts or notes.

Existing Privacy and Terms routes are linked and unchanged. **The existing `/Home/Privacy` view contains placeholder English text.** Epic 22 code commit is allowed, but **public launch remains blocked until Privacy content is completed**, and **municipal outreach remains blocked until Privacy content is completed**. A separate approved privacy-content task is required. This Epic does not draft legal terms.

No GPS, Walks, bin/photo/importer, Place model, Saved Places, Amenities, DataSource, Featured, Community, Privacy Zone, schema or auth configuration changes.

## Verification

- Build and Razor compilation: PASS. Existing MailKit/MimeKit NU1902 and Home/Planner nullability warnings remain.
- .NET: 837/837 PASS, 51 new cases. Covers actual anonymous HTTP pages, SEO/OG, preview/host guards, contact encoding/injection/missing configuration, navigation/landmarks, factual content and database-free rendering. Final contact regressions reject consecutive/leading/trailing local-part dots, 65-character local parts and malformed domains in both the parser and actual pages; the 64-character boundary remains valid. Existing admin-mailbox and CR/LF cases remain covered. Existing sitemap expectations include the two intended new URLs.
- Node: 82/82 PASS, including Home/Active inline syntax. All 12 standalone JavaScript files pass syntax checks. No new JavaScript.
- Layout: 16/16 new-page cases at 320, 375, 390, 430, 768, 1024, 1440 and 1920 pixels. Checks overflow, hero CTA visibility, navigation overlap, landmarks, section targets and keyboard skip/focus behavior. Desktop/mobile screenshots inspected. Offline fixtures block external assets and therefore also exercise font fallback.
- App presentation regressions: 18/18 Home/Discovery/Details cases PASS.
- Isolated PostgreSQL 17: 6/6 PASS, reusing the prior SEO harness with expected static URL count updated. Invalid-row filtering, one minimal query, Details/friendly routes, stored lastmod and 2,501-Place scale remain valid. Model-created temporary database deleted; server stopped. No migrations applied.
- Tracked/untracked whitespace and schema-scope checks: PASS.

Local review artifacts are outside the repository in `C:\Codex\epic22-review`; layout and PostgreSQL harnesses are under `C:\Codex`. No generated artifacts are part of the intended change.

Remaining manual checks: public mailbox delivery/monitoring, approved Privacy content, actual deployed proxy/crawler headers, real-device/PWA handoff and screen-reader experience. No outreach messages were sent.

## Intended file scope (18)

Modified (7):

- `DoggyDrop.Tests/SeoHttpTests.cs`
- `DoggyDrop.Tests/SeoTests.cs`
- `DoggyDrop/Controllers/SeoController.cs`
- `DoggyDrop/Services/SeoMetadata.cs`
- `DoggyDrop/Views/Home/About.cshtml`
- `DoggyDrop/Views/Shared/_Layout.cshtml`
- `DoggyDrop/appsettings.json`

New (11):

- `DoggyDrop.Tests/ProjectPageTests.cs`
- `DoggyDrop/Controllers/ProjectController.cs`
- `DoggyDrop/Services/PublicContact.cs`
- `DoggyDrop/Views/Project/Index.cshtml`
- `DoggyDrop/Views/Project/Municipalities.cshtml`
- `DoggyDrop/Views/Project/_Contact.cshtml`
- `DoggyDrop/Views/Project/_ViewStart.cshtml`
- `DoggyDrop/Views/Shared/_ProjectLayout.cshtml`
- `DoggyDrop/Views/Shared/_SeoHead.cshtml`
- `DoggyDrop/wwwroot/css/project.css`
- `docs/project-website.md`
