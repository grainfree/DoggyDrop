# Epic 23 launch readiness

Baseline: `85aaad50c73325d915166cd5ec646f96d51d7d8f` (`Harden Identity account security`). Scope is launch UI/PWA/response hardening, not new product semantics. Implementation remains unstaged for owner review. No deployment or production access was used.

## Audit and decisions

| Area | Evidence before editing | Decision |
|---|---|---|
| First run | Existing nonmodal intro, v1 dismissal, storage-failure handling, Active Walk bypass | Preserve the implementation/key; refresh three short bullets for bins/water/Places, planning and community evidence. |
| Location | Actual Home initialization called `focusMapNearUser` without an action | Remove that ordinary-browsing call. Existing active recording keeps its previous behavior. Nearest/directions/confirmation/Smart Walk requests remain contextual. Improve Home permission-failure text without changing routing or GPS. |
| Install/PWA | Manifest and valid icons; generic install toast; no worker | Replace toast with explicit menu guidance/action; add offline-only worker. |
| Cache/privacy | No previous SW cache; dynamic responses did not universally prohibit caching | Private/no-store/no-cache for all dynamic responses, including anonymous HTML carrying tokens; public static assets handled separately. |
| Headers | Identity protection existed; general anti-framing/nosniff/permissions/HSTS absent in app | Conservative application headers, preserve stronger Identity policy. No claims about unobserved proxy configuration. |
| Errors | Generic MVC 500; error layout can require database reads; empty 404/403 | Dependency-free branded HTML for GET requests accepting HTML; preserve status codes and API errors. |
| Health | Existing minimal `/health` | Retain unchanged, with no external/DB liveness dependency. |
| Assets | Application cache busting exists; common vendors unversioned | Version shared Bootstrap/jQuery/validation assets; revalidate unversioned assets and worker/manifest. No bundler/compression rewrite. |
| Home/performance | Existing public projections, grouped trust SQL, clustering | Retain query/algorithm shape, rerun scale/privacy fixtures. |
| SEO | Existing safe origin, metadata, robots, sitemap, JSON-LD | Refresh only stale Home description; regression tests, no replacement SEO system. |
| Accessibility | Short-height browser tests found count badge over intro CTA and tablet vertical rail over menu | Hide count badge while intro is visible; five-column ordinary action rail only in short tablet landscape. Active Walk layout untouched. |
| Identity | Protected, reviewed baseline | Run all regressions; no handler, limiter, token or authentication semantic edits. |
| Legal | Privacy is still placeholder text | Public launch and municipal outreach remain blocked pending separate owner/legal completion. Do not treat this engineering Epic as legal approval. |

## IMPLEMENTED: first run, permissions and install

Existing `doggydrop.homeIntroDismissed.v1` stores only `true`. Returning visitors remain dismissed; no re-onboarding version bump. Disabled storage degrades to a dismissible per-page intro. It neither takes initial focus nor blocks map access; dismissal can focus the map. No account or location gate.

Ordinary initial Home no longer requests geolocation. Explicit nearest/location/navigation actions request it; denial/timeout/unavailable leave map browsing usable and allow user-initiated retry. Existing active-recording and navigation watchers, Smart Walk start, legacy Planner behavior and one-shot confirmations are unchanged. Confirmation still requires accuracy <=25m and distance + accuracy <=50m. No new notification permission or push backend. Existing in-app notification retrieval is not browser push.

Install guidance lives in Menu → Namesti DoggyDrop. A captured Chromium `beforeinstallprompt` is used once, only on button activation. Dismissal does not trigger a new prompt; unsupported browsers get menu guidance; iPhone/iPad get Safari Share/Add to Home Screen text. Standalone detection uses the display-mode media query and the iOS flag. No install CTA when already installed. No new install preference/tracking storage.

Manifest has consistent DoggyDrop names, root start/scope, standalone display, existing portrait orientation, white background and matching layout theme. PNG sizes are 192/512 and Apple 180. Icons remain purpose `any`: no claim of maskable safe-zone suitability. Physical OS icon cropping/installability still requires devices.

## IMPLEMENTED: service worker and updates

`/sw.js` caches **only** `/offline.html` and `/css/pwa.css` in the versioned `doggydrop-offline-v1` cache. These are public, user-independent files. Everything else is network-only: navigation responses are never stored; failed GET navigation receives the public offline document. API/media/tile/provider requests are not cached. Every non-GET bypasses the worker. No queues, background sync or POST replay.

Private Profile, Dogs, Walks, Saved Plans, contributions, Identity and Admin pages cannot be served from this application cache. A → logout → B/offline browser fixtures use synthetic private documents; actual authorization/IDOR and cookies are covered separately by existing HTTP tests. This is not a guarantee about arbitrary browser history screenshots/BFCache; actual logout/Back on Safari is an owner check. Response no-store adds defense beyond the SW exclusion.

The offline document explains that fresh map data, routes, Smart Walk generation and submissions need internet. Its recovery link opens Home with GET; returning online never resubmits a form. An in-page dismissible status message explains interrupted connectivity without reloading or discarding form values. No fake offline maps or routes.

Worker installation has no third-party dependency. Activation deletes only old caches with the `doggydrop-offline-` prefix. It does not clear cookies, local/sessionStorage or unrelated caches. No `skipWaiting`, `clients.claim`, automatic reload or update timer. A waiting update is explained in the menu: finish the current task, close all app tabs/windows and reopen. Active walks/uploads/password changes/confirmations cannot be automatically reloaded by this code. Online pages/assets stay fresh independently because the SW does not cache them. Developers must bump the offline cache version when changing offline assets.

Lifecycle basis: [W3C Service Worker explainer](https://github.com/w3c/ServiceWorker/blob/main/explainer.md) and [MDN lifecycle](https://developer.mozilla.org/en-US/docs/Web/API/Service_Worker_API/Using_Service_Workers). No forced takeover is needed for an offline-only cache.

## IMPLEMENTED: headers and reliability

| Control | Final application behavior | Limits |
|---|---|---|
| CSP | `base-uri 'self'; object-src 'none'; frame-ancestors 'none'` | Deliberately does **not** constrain scripts, styles or connections. No unsafe-eval or wildcard script policy added. |
| Frame protection | CSP ancestors none + matching X-Frame-Options DENY | No legitimate framing use found. |
| nosniff | X-Content-Type-Options nosniff | Applied to static and dynamic responses. |
| Referrer | General strict-origin-when-cross-origin header and matching layout meta | Identity retains no-referrer. |
| Permissions | geolocation and camera self; microphone/payment/USB disabled | Native file picker remains; physical Safari camera choice requires owner test. |
| HSTS | Non-Development, HTTPS requests after existing forwarded-header middleware; framework default 30 days, no preload/subdomain expansion | Real proxy trust/TLS termination remains deployment verification. No HTTPS redirect loop introduced. |
| Dynamic caching | private, no-store, no-cache plus Pragma no-cache | Includes private responses and anonymous dynamic HTML; existing Admin export protection retained. |
| Static caching | Versioned first-party URLs can use a one-year immutable lifetime; unversioned URLs revalidate | Worker, manifest and offline document always revalidate, even with arbitrary query parameters. |
| Identity | Existing no-referrer/no-store preserved | No authentication, antiforgery, password, lockout, email budget or linking changes. |
| Errors | Branded generic 403/404 HTML for GET HTML requests; production 500 HTML or generic JSON with original status | No request path, exception, SQL, token or user data interpolated. Existing AccessDenied Identity page remains. |
| Health | Minimal existing application responsiveness JSON | No dependencies or secret details exposed. |

A strong script CSP needs a separate bounded migration of inline Razor scripts, event attributes and dynamic styles to external modules/nonces, followed by a real resource allowlist. Major existing inline surfaces include Home, Active, Planner, shared notification UI, contribution/admin/Identity validation. This Epic does not claim a comprehensive XSS policy. [Microsoft HSTS/forwarded-header guidance](https://learn.microsoft.com/en-us/aspnet/core/security/enforcing-ssl) informs the middleware ordering.

No new operational logs, request/body logging, analytics, cookies or private location persistence. Normal routing logs remain status/type rather than geometry. Existing provider-exception/logging operational limitations from the Identity audit remain. Current-tree credential scan emits locations only; no values. Hosting compression/proxy headers were not observed, so no duplicate application response compression was introduced.

Existing database behavior remains: migration command support and startup `Database.Migrate`/seed already exist. They were not added or run through application startup here. Disposable loopback PostgreSQL regression databases exercise existing migrations, constraints and transactions; no production schema was accessed.

## Configuration inventory — key names only

- Database: `ConnectionStrings:DefaultConnection`, `DATABASE_URL`.
- Canonical/indexing: `Seo:PublicOrigin`, `Seo:AllowIndexing`, `IS_PULL_REQUEST`.
- Initial bootstrap only: `AdminBootstrap:Email`, `AdminBootstrap:Password`; existing administrator behavior unchanged.
- Development/test seeding: `SeedTestUser:Enabled`, `SeedTestUser:Email`, `SeedTestUser:Password`; not a production credential-rotation mechanism.
- Walking provider: `OpenRouteService:ApiKey` (existing supported aliases remain).
- Basemap: `Basemap:Provider`, `Basemap:CartoApiKey`; only reviewed restricted browser-public tile key can reach browser. No provider activated here.
- Google: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret` (existing aliases remain). Missing pair disables provider.
- Cloudinary: `Cloudinary:CloudName`, `Cloudinary:ApiKey`, `Cloudinary:ApiSecret` (existing aliases remain).
- R2: `CloudflareR2:AccountId`, `CloudflareR2:AccessKeyId`, `CloudflareR2:SecretAccessKey`, `CloudflareR2:BucketName`, `CloudflareR2:PublicBaseUrl`, `CloudflareR2:Endpoint`.
- Email: `EmailSettings:SmtpServer`, `EmailSettings:SmtpPort`, `EmailSettings:SmtpUser`, `EmailSettings:SmtpPass`, `EmailSettings:SenderName`, `EmailSettings:SenderEmail`, `EmailSettings:AdminEmail`; existing Username/Password aliases remain.
- Public contact: `PublicContact:Email`.

No configuration values were inspected/copied. Existing missing-key ORS safe failure and OSM default/fallback remain. Required database configuration still follows existing startup failure; no insecure defaults added.

## Storage and third parties

| Storage | Purpose / sensitivity / lifetime |
|---|---|
| Identity/antiforgery/TempData/correlation cookies | Existing protected framework state; HttpOnly/Lax application cookie, production Secure, 14-day sliding ticket and ~30-minute stamp validation unchanged. External provider state is temporary. |
| `doggydrop.homeIntroDismissed.v1` | Existing per-device boolean dismissal, until removed/version changed. No identity/location. |
| `doggydrop.map.*`, `doggydrop.notifications.*` | Existing browser presentation preferences, no expiry contract. |
| `doggydrop.walk.autoCompleteStops`, `doggydrop.walk.vibrateOnStop` | Existing walk preferences. |
| `doggydropPlannerLocation` | Existing legacy Planner coordinate preference, persistent localStorage. This Epic introduces no new coordinate persistence. Do not claim all client storage is location-free. |
| `doggydropPlannerGpsTried` | Existing per-tab/session Planner state. |
| `doggydrop.walk.binAlerts.{walkId}` | Existing per-tab walk/bin alert state; not removed by SW cleanup. |
| `pwaPromptShown` | Old install-toast key may remain inert; new code no longer reads/writes it. |
| Cache Storage | New public offline document and CSS only. No user IDs, routes or private pages. |

Browser runtime dependencies remain OSM/CARTO/dormant Stadia tiles; Google Fonts; jsDelivr Bootstrap icons; unpkg Leaflet; managed/public media. Existing weather views may request Open-Meteo. Server integrations can include ORS, Google OAuth, Cloudinary/R2/local media and SMTP, plus hosting/PostgreSQL. Production provider/region/processor agreements are not proven by source code. No tracking SDK or cookie banner added; no legal compliance conclusion inferred.

Failure behavior remains: ORS unavailable → safe routing failure with map browsing; CARTO tile failure → OSM with correct attribution; broken media → existing logo/category/photo fallback; SMTP/OAuth unavailable → corresponding account flow limitation. No live provider call was made to test these.

## Performance, SEO and accessibility

Home core anonymous map work is bounded by query type: bins with DataSource join, projected Places, two bin-trust queries, one water-trust aggregate, public WaterPoint projection. It does not query per marker. Authenticated layout/weekly-goal/activity work is additional existing behavior, not claimed to be exactly six total request queries. Trust SQL uses GROUP BY/MAX/COUNT DISTINCT and never materializes lifetime confirmation history. Map query/DTO semantics were not changed.

Local synthetic anonymous Home capture is approximately 209 KB uncompressed; includes substantial existing inline HTML/CSS/JS. This is not a production 1,272-WaterPoint payload measurement. Shared assets include Bootstrap minified CSS (162,726 bytes), site CSS (23,623 bytes), minified jQuery (89,503 bytes) and Bootstrap bundle (78,474 bytes). Vendor distributions also contain unused unminified/RTL alternatives; they are not all requested by shared layout. No build-system rewrite or speculative asset deletion. Existing Place logos use managed 128px transformations/fallback; uploaded-bin image delivery unchanged. Images/retina/cluster fixtures remain the regression authority.

Home title: “DoggyDrop – zemljevid za sprehode s psom”. Description: “Najdi koše, pitnike in uporabne pasje lokacije, načrtuj sprehod in pomagaj skupnosti.” Canonical is root regardless of arbitrary focus/filter query. Validated public origin remains authoritative for canonical, OpenGraph, sitemap and Identity mail links. Existing robots/sitemap keep private/Admin, inactive/unsupported Places and preview hosts out of intended indexing. Public Place names/category/coordinates remain encoded, with default JSON escaping protecting JSON-LD. No standalone bin/water sitemap inflation.

Slovenian language, main/navigation landmarks, native labelled controls and pinch zoom retained. New PWA/error controls use readable text, visible focus and 44px targets, dark green/white contrast, no animation dependence. Browser fixtures cover short heights, safe-area simulation, 125% text and reduced motion. Physical screen-reader behavior is not claimed. Landscape fixes apply only to ordinary Home.

## VERIFIED LOCALLY

Exact results and measured timings are recorded in `C:/Codex/epic230/` and summarized below. These are local synthetic evidence, not production performance guarantees. No fragile millisecond assertions.

## REQUIRES PHYSICAL DEVICE / DEPLOYED ENVIRONMENT

Owner checklist (do not run these production checks as part of this Epic):

1. iPhone Safari first Home: no automatic location prompt; dismiss intro and revisit.
2. Explicit location permission, denied/poor-signal recovery, nearest bin and nearest WaterPoint.
3. Smart Walk 15/30/45/60 minute routes and one Start; physically assess route quality.
4. Confirm a real nearby bin and WaterPoint; verify too-far rejection.
5. Submit a real photo; preserve picker, upload feedback and pending-review result.
6. Profile → Change Password; password-manager autofill/paste; normal login/logout/Back.
7. Legitimate Google sign-in, explicit account linking, safe unlinking; real email delivery and canonical links.
8. Install on iPhone; standalone safe areas/bottom nav; Android install where available; OS icon cropping.
9. Offline and return online; private pages absent from SW cache; no automatic POST retry.
10. On a later deployment, finish active tasks, close all tabs/windows and reopen for waiting update; no forced reload.
11. Current infrastructure map, required attribution, CARTO → OSM fallback where practical, real ORS/media delivery.
12. Mobile keyboard on login/password/contribution forms; VoiceOver/TalkBack; pinch zoom and toolbar transitions.
13. Hosting HTTPS/forwarded-scheme/HSTS and compression; effective IP mail budgets. Do not assume proxy settings from local tests.
14. Complete and approve Privacy/controller/legal/retention/provider facts before public launch or municipal outreach.

## FUTURE IMPROVEMENT / remaining limitations

- Strong script CSP remains a separate migration; current CSP is intentionally limited to base/object/framing protection.
- Two accepted Identity LOW observations remain: minor account enumeration differences, generic errors for some malformed redirect/token inputs.
- Offline support is only a public recovery state. Browser storage eviction and OS/browser installation/update differences remain platform limits.
- Physical-device/provider tests and the separate Privacy legal launch blocker remain owner work.
- Existing MailKit/MimeKit NU1902 warnings are unchanged dependency debt, not fixed by this Epic.

## Final local verification

- Build PASS; full .NET **1,662/1,662**, zero skipped/failing. Existing four MailKit/MimeKit NU1902 build warnings unchanged.
- New launch HTTP/header/error/static-cache tests **20/20**.
- Identity: bootstrap **24**, password settings **33**, external-login **43**, adversarial **52**; privacy HTTP/IDOR/deletion **63**, all pass. Broader privacy suites also pass in the full run.
- SEO **111** (77 HTTP + 34 unit); routing **53** (26 HTTP + 27 unit); contribution **100**; confirmation **62**; WaterPoint **42**; Smart Walk **92**; Finish **89**; bin import **50**; Place import **45**, all pass within the full run. Other Admin/Place/media/GPS regressions are included in the complete TRX.
- Full Node **268/268**. Focused PWA **24/24**, retained/updated intro **14/14**.
- New launch browser **56/56** layouts (seven widths, two heights, two safe-area simulations, two text scales; reduced motion) + **22/22** flows. Real local worker install/activation/update/offline/A-to-B cache behavior; explicit geolocation denied/unavailable/timeout/retry; emulated pinch zoom.
- Home **196** layouts, **16** lifecycle, **4** large datasets; WaterPoint **168** layouts, **32** filters, **4** user-marker lifecycle, **4** large datasets.
- Confirmations **168** layouts, **70** flows, **7** Admin layouts. Smart Walk **92** layouts, **60** lifecycle, **14** Saved, **18** submission, **14** error layouts, **3** XSS, **4** geometry performance.
- Contributions **54** existing layouts, **108** mobile/safe-area, **108** submit/retry/keyboard, **4** Home request cases. Identity **40** layouts/accessibility + **3** referrer probes.
- Place icons/public surfaces/logo/popup **198**, final gallery **264**; navigation **144** layouts + **36** resize/reset; attribution **96**, all pass.
- Isolated PostgreSQL 17: confirmations/migration/concurrency/performance **16/16**, Smart Walk **8/8**. Only disposable loopback DBs; existing migration up/down regression was local, not production.
- JS syntax **31/31** (standalone, worker, rendered current Home/Planner and existing Active substitutions); new browser fixture syntax PASS. Count decreased from 33 because the old inline install script was removed from four rendered captures and two standalone PWA/worker files were added.
- Explicit Razor rebuild and RazorCompile PASS. Git whitespace PASS. Secret scan: **703 text files**, 22 unchanged previously reviewed candidate locations, no new credible production-capable secret; no values output.

Local performance (no pass/fail timing thresholds):

| Dataset | Result | Observed local duration |
|---|---|---|
| Home 250/500/1500/3000 markers | Count conservation, no loss/duplication | Initial fixture 265/271/319/338 ms; rebuild 6/8/21/36 ms |
| Water 500/1000/1500/2500 plus 600 bins/Places | 1100/1600/2100/3100 total preserved | Initial 293/295/310/342 ms; repeated refresh 145/278/442/746 ms |
| Trust 250/500/1500/3000 infrastructure | 3 SQL queries; 10k/50k/50k/50k histories | 105/141/289/273 ms |
| Smart Walk 250/1500/5000/20000 vertices | One route layer, bounded request/lifecycle behavior | Render fixture 111/114/111/162 ms |

Evidence: `C:/Codex/epic230/results/final-all.trx`, browser subdirectories, `final-node.log`, `syntax-results.json`, `razor-rebuild.log`, `razor-compile.log`, `postgres-confirmation.log`, `postgres-smart.log`, `inventory.json`. Screenshots: `launch-browser/install-390.png`, `launch-browser/offline-390.png`. They remain outside the repository.

Final code review: **BLOCKER 0, HIGH 0, MEDIUM 0**. One Epic 23 LOW limitation: partial CSP without script restrictions; two accepted pre-existing Identity LOW findings remain unchanged. Physical/deployment validation and the separate Privacy legal launch blocker are not asserted complete. No schema/model/snapshot/migration edits, no protected algorithm or evidence-semantics changes. Only ordinary Home permission timing and presentation changed.

## Exact repository scope

**19 files: 10 modified + 9 new**. No external captures, DBs, logs or helpers included.

- MODIFIED `DoggyDrop.Tests/JavaScript/home-intro.test.cjs`
- MODIFIED `DoggyDrop.Tests/JavaScript/home-map-locations.test.cjs`
- MODIFIED `DoggyDrop/Program.cs`
- MODIFIED `DoggyDrop/Services/SeoMetadata.cs`
- MODIFIED `DoggyDrop/Views/Map/Index.cshtml`
- MODIFIED `DoggyDrop/Views/Shared/_Layout.cshtml`
- MODIFIED `DoggyDrop/Views/Shared/_ValidationScriptsPartial.cshtml`
- MODIFIED `DoggyDrop/wwwroot/css/home-intro.css`
- MODIFIED `DoggyDrop/wwwroot/manifest.json`
- MODIFIED `docs/privacy-data-audit.md`
- NEW `DoggyDrop.Tests/Browser/launch-readiness.cjs`
- NEW `DoggyDrop.Tests/JavaScript/pwa.test.cjs`
- NEW `DoggyDrop.Tests/LaunchReadinessTests.cs`
- NEW `DoggyDrop/Services/LaunchReadiness.cs`
- NEW `DoggyDrop/wwwroot/css/pwa.css`
- NEW `DoggyDrop/wwwroot/js/pwa.js`
- NEW `DoggyDrop/wwwroot/offline.html`
- NEW `DoggyDrop/wwwroot/sw.js`
- NEW `docs/launch-readiness.md`


## Epic 24.0 privacy/legal gate (2026-10-04)

The factual Privacy/Terms drafts, owner-only data export, account deletion and new-upload metadata controls are undergoing technical review. This does **not** approve a public launch or municipal outreach. Both remain **BLOCKED** until the controller/address, legal bases, retention, children/rights procedures, provider arrangements, final Terms/photo permission and ODbL decisions in [privacy-legal-review.md](privacy-legal-review.md) are resolved. See [privacy-data-audit.md](privacy-data-audit.md) for current technical behavior; older milestone counts above are historical. No policy is represented as legally approved and no production action is part of this change.
