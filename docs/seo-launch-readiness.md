# Epic 21 — SEO and launch readiness

Started from clean main at efa641e (Epic 20). No migration, production access, deployment, commit or push is part of this Epic implementation.

## Public surface audit and indexing policy

| Surface | Access / indexing decision |
| --- | --- |
| `/`, `/Map`, `/Map/Index` | Public Home; INDEX for anonymous production requests. All canonicalize to `/`, including query variants. No routing/first-run change. |
| `/Places`, `/Places/Index` | Public Discovery; INDEX with `/Places` canonical. Client filters/query strings do not create SEO landing pages. |
| `/Places/Details/{id}` | Public compatibility URL; renders 200 with friendly canonical. Existing links and Save return paths still work. |
| `/lokacije/{id}/{slug}` | Public eligible Details; INDEX, self-canonical. Wrong, missing, historical or extra path slug redirects locally with 301 using the authoritative ID. |
| `/SavedPlaces` | Authorized personalized list; NOINDEX; excluded from sitemap. |
| `/Walks` (history), Active, Details/memory and planner | Authorized; NOINDEX, no public/indexable walk pages or GPS metadata. |
| `/Home/UserProfile`, Settings | Authorized; NOINDEX. |
| `/Home/Community` | Actual controller requires authentication; live/personalized content stays NOINDEX. |
| Identity login/register/manage | NOINDEX; existing authentication remains authoritative. |
| Admin Places, provenance/bulk/import, bin administration | Authorized; NOINDEX. |
| APIs, health, notifications and mutations | NOINDEX response header; existing authorization/status codes unchanged. |
| `/Map/Add`, find-bin utilities | Public utility flows stay accessible but NOINDEX; no individual bin SEO pages. |
| `/Home/Index` | Legacy separate map view; NOINDEX, not an additional Home search landing page. |
| About, Help, PwaHelp, Privacy, Terms | Existing utility/static pages remain accessible but NOINDEX for this conservative launch scope. Future project content can opt in deliberately. |
| Missing/inactive/unsupported/invalid Place | 404, NOINDEX, no JSON-LD. |
| 500 / `/Home/Error` | 500, NOINDEX. Added the missing exception-handler target with generic Slovenian copy, no internal exception details. |

Only anonymous successful HTML pages explicitly opting in get `index, follow`. Authenticated variants get `noindex, nofollow` while retaining the same public canonical and factual JSON-LD. All other dynamic responses default to noindex via middleware, including authorization challenges and empty 404s. Public image/CSS/JS/font assets remain crawlable. Robots directives are not access control.

## URLs, origin and environments

`Seo:PublicOrigin` defaults/configures `https://doggydrop.app`; it must be a bare HTTPS DNS origin without credentials, query, fragment or nonstandard port. All absolute canonical, OG, sitemap and JSON-LD URLs derive from it, never request/forwarded headers. Slugs use bounded (80-character) lowercase ASCII, transliteration of č/š/ž and collapsed punctuation; `lokacija` is the nonempty fallback. There is no slug column or history table.

Production indexing additionally requires `Seo:AllowIndexing=true`, `IS_PULL_REQUEST` not true, and the incoming authority matching the configured origin. SEO middleware runs before forwarded headers and captures the incoming authority only as an indexing gate. Existing forwarded-header trust configuration is unchanged; an arbitrary X-Forwarded-Host cannot switch preview indexing on or poison generated URLs.

Development, Staging, explicit opt-out, Render PR previews, onrender.com and other alternate hosts return disallow-all robots and noindex dynamic responses. They publish an empty sitemap. Canonicals still point to the configured origin. Staging deployments should explicitly set `Seo__AllowIndexing=false` even when copying Production environment settings. `IS_PULL_REQUEST` follows Render's documented preview flag: https://render.com/docs/environment-variables . Public deployment configuration/headers must be verified by the operator after deployment; production was not contacted.

`robots.txt` deliberately allows crawling on canonical production so crawlers can observe noindex directives on utility/login pages, and references the absolute sitemap. Sitemap responses use application/xml and no-store, with one projected ID/name/UpdatedAt query and no amenities/Saved/source/logo loads. Eligibility is shared with Details: active, supported, nonempty bounded name without control characters, finite coordinates within existing latitude/longitude bounds. UTC lastmod uses stored UpdatedAt (omitted for an unset timestamp), never the current request time. No priority/changefreq, doorway pages, sharding or new indexes. Revisit standard sitemap size/URL limits before reaching 50,000 URLs; thousands fit the current simple format.

## Metadata and factual structured data

One shared Layout produces one title, at most one description, canonical, OG title/description/url/type/image and a simple Twitter summary card. Razor encodes attributes. Place descriptions use only name/category and a neutral invitation to view location/directions, not arbitrary description HTML, unverifiable contact availability or amenity lists. Featured/Saved never influence SEO claims, canonical or JSON-LD.

JSON-LD uses default System.Text.Json escaping, including script-sensitive characters. Schema types verified against official definitions:

- Veterinarian: VeterinaryCare — https://schema.org/VeterinaryCare . This is an Organization subtype, so geo belongs under location: Place.
- PetShop: PetStore — https://schema.org/PetStore .
- Groomer/DogSchool: LocalBusiness — https://schema.org/LocalBusiness .
- DogFriendlyCafe: CafeOrCoffeeShop — https://schema.org/CafeOrCoffeeShop .
- DogPark: Park — https://schema.org/Park .
- DogBeach: Place — https://schema.org/Place .

Only name, canonical URL, stored plain address, valid public telephone, public Place coordinates and optional validated managed public logo are serialized. No invented address components, rating/review/price/hours, promotion/recommendation signals, DataSource fields, amenity verification/source, user GPS, saved relation or organization contacts.

Social images use the existing validated managed logo where suitable, otherwise the existing 512px branded PNG (`/images/icon-512.png`, approximately 253 KiB). Arbitrary external ImageUrl values have no availability/provenance guarantee, so they are conservatively not used as social/JSON-LD images; no server fetch or SSRF path is added. Existing Details photos are unchanged. Managed asset availability cannot be guaranteed without live-provider access; no live Cloudinary/R2 requests were made. No image-generation dependency or new branding asset.

## App preservation and visitor UX

Details remains anonymous, with name/category/address, existing map/directions and confirmed amenities; adds one `Razišči lokacije` action. Direct visits never run Home intro. Existing Save controls and destinations remain compatible. Metadata does not alter Featured presentation or ordering, distance sorting, GPS, walk behavior, bin/import semantics, Admin concurrency or authentication.

Existing manifest retains name/short_name, start_url `/`, scope `/`, standalone display, colors and icons. Document language remains `sl`; an explicit favicon link reuses the existing app icon. No service worker file/registration/cache was found in the repository, so none was added or redesigned. Existing install prompt stays intact. No analytics, verification placeholder, third-party SEO SDK, hreflang, company data or security-header relaxation.

## Verification and remaining manual checks

Tests cover hostile names/script boundaries, real HTTP metadata, legacy/friendly/malformed/renamed URLs, host and forwarded-host attacks, nonproduction/preview gates, authentication/noindex, 404/500, personalized metadata invariance, slug edge cases, schema mapping and minimal sitemap query. Isolated PostgreSQL checks cover NaN/infinity, matching Details eligibility, original lastmod and one-query scale with 2,501 eligible Places. Databases are built from the model and deleted; no migrations execute. The normal Program startup runs migrations, so all HTTP testing uses isolated hosts instead.

Remaining manual launch checks: canonical-domain/proxy/environment configuration after deployment, Search Console/Bing sitemap submission using real credentials, social-crawler image availability, native mobile navigation/PWA install and screen-reader experience. No search-engine indexing or external rich-result eligibility is promised.


Final local results: build and explicit Razor compilation PASS; 786/786 .NET (78 new SEO cases), 82/82 Node, 6/6 isolated PostgreSQL, 18/18 offline layout cases, 12 standalone JS plus Home/Active inline syntax, and tracked/untracked whitespace checks PASS. Existing package/nullability warnings remain. PostgreSQL was stopped. No migration/snapshot change. Complete intended scope: 13 files (6 modified, 7 new).
