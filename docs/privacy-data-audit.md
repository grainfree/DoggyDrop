# Epic 22.1 — Privacy data audit and owner decision register

**NEEDS OWNER INPUT — not a Privacy Policy, not approved legal text, not a launch clearance.**

Audit date: 2026-09-28 (this is an audit date, not a policy effective date). Baseline: clean `main` at `760de2fa58091c0bb7cfd9fd07d38f5306421d6f`, after Epic 22. This document records repository behavior and explicitly supplied owner facts. It does not infer production records or deployment settings. The public Privacy and Terms views are unchanged. Public launch and municipal outreach remain blocked pending an accurate, approved notice and resolution of the decisions below.

## 1. Confirmed facts and unresolved gate

Owner-confirmed in this task:

- Controller: **Jernej Furman s.p.**, represented by Jernej Furman, Slovenia. This is an owner statement, not a registry verification.
- `admin@doggydrop.app` is the monitored privacy/GDPR request address as well as the configured public contact. The origin is `https://doggydrop.app`.
- The web application runs on Render; the application database is PostgreSQL. Earlier owner confirmation: one web instance, no autoscaling.
- There is **no approved general legal-basis mapping, retention schedule or child/age policy**.
- Production media backends, SMTP provider, regions, contractual roles, DPAs and transfer safeguards have **not been verified by the owner**.

Required decisions/information before finalizing the Slovenian notice:

1. Supply the actual official registered business/postal address intended for the notice. The supplied insertion placeholder is not an address. Confirm any other required public controller details and whether a DPO/representative is applicable; none is invented here.
2. Approve a lawful-basis decision for each purpose in section 8. Browser location permission, an optional feature toggle, agreement to Terms, and Google OAuth approval are not automatically a controller's GDPR legal-basis decision.
3. Define retention periods or meaningful criteria for all persistent categories, including inactivity, closed accounts, contributions, social data, media originals/copies, logs, support/outreach correspondence and backups. State exceptions and the operational means of enforcing them.
4. Decide and implement/document the complete rights-request, export and deletion process. Current self-service limitations are material (section 6); no promise that deleting an account removes everything is supportable.
5. Verify current providers and regions for Render, PostgreSQL, every active/legacy image backend and SMTP. Confirm roles, agreements, applicable onward providers and any international-transfer safeguards/access to them. A provider SDK or public endpoint alone proves none of these legal facts.
6. Approve child/age treatment. No registration age check or date of birth was found; no minimum age is assigned here.
7. Review the existing sharing behavior: public leaderboard identity/email fallback; social display of completed-walk information/photos; public playdate information; Nearby dog visibility; limits of Privacy Zone. Decide any separately authorized implementation corrections before approving claims inconsistent with these facts.
8. Review Terms and privacy wording together. Existing Terms contain broad statements about minimal data, disclosure only with consent, and promotional photo reuse. These do not establish an approved legal basis and must be reconciled with the verified flows. No Terms rewrite was performed.

## 2. Data categories matrix

“Unknown” retention means no general expiry/deletion schedule was found or approved, not that indefinite retention is legally acceptable. All server-stored personal data can reach the configured hosting/database infrastructure. “Public” distinguishes anonymous access from disclosure to other signed-in users. Deletion descriptions are code capabilities, subject to section 6, not policy promises.

| Category | Source and purpose | Stored? | Public / shared? | Other third-party flow | Retention known? | Delete path known? |
|---|---|---|---|---|---|---|
| Account / credentials | Email/password registration or external login; authentication and account management | Identity user, normalized identifiers, password hash for local accounts, confirmation/security/lockout fields, external login provider/key; optional phone and 2FA data | Account settings restricted; display name/photo and sometimes email fallback used socially and in public rankings | Google login; configured SMTP for account mail | General schedule unknown; cookie tickets separately below | Framework account deletion exists but can fail on dependent rows; no complete cleanup/export |
| User profile | User edits display name and profile image; external signup derives display name from claims/email prefix | `DisplayName`, `ProfileImageUrl`; email/optional Identity phone | Name/photo shown on social surfaces; public ranking fallback can reveal email | Configured image storage and direct image delivery | Unknown | Editing/replacing is available; automatic old-image purge not established |
| Dog profile | Owner enters name, breed, age in years, gender, size, character, image, map icon, visibility, approximate city selection | Dog fields, owner ID, created time, selected city coordinates/update time | Owner management; visible/friends-only Nearby rules; names/photos/statistics also used in social features | Image provider/delivery | Unknown; Nearby freshness is not deletion | Edits and location clearing; successful account deletion cascades dog rows; no dedicated dog-delete action found |
| Walk summaries | Owner starts/finishes walk with dog; progress/history | Start/end, status, distance, used-bin count, owner/dog/optional plan IDs | Owner history; completed summaries shared to signed-in Community; public city rankings use derived totals | Infrastructure; user-initiated sharing | Unknown; stale closure is not erasure | No dedicated completed-walk deletion action found; successful account deletion cascades |
| Precise WalkPoints | Active/Home browser recorder posts latitude/longitude/time; route and distance | Walk ID, latitude, longitude, recorded time. Accuracy is processed for acceptance but is not a WalkPoint column | Exact route geometry restricted to owner; selected aggregate uses described below | Map/routing flows depend on feature; infrastructure | Unknown | Successful parent deletion cascades; Privacy Zone does not erase points |
| Planned routes / park visits | Planner location/options; saved routes and explicit park check-ins | Plan title/area/options/times/stops/geometry, optional dog; visit owner/dog/place/name/coordinates/time | Plan detail/geometry owner-filtered; park counts/achievements and social stop labels may be shared | Overpass for stop discovery; configured OpenRouteService foot-walking for planning | Unknown; history/display windows are not expiry | Owner DeletePlan removes plan and dependent geometry/stops; visits have no dedicated self-service delete found |
| Walk photos / captions | Uploaded photo, optional caption and planned-stop association | Image URL, user/walk/stop IDs, caption/time; external/local bytes | Completed photos shared with signed-in users; delivery URLs are not authenticated private-file routes | Cloudinary/R2/local image delivery; optional user-initiated browser share/download | Unknown, including originals/caches | DeletePhoto removes DB row/reactions, not stored asset; account cascade likewise does not call media cleanup |
| Saved Places | Signed-in user bookmarks public Place | Composite user/place relation and SavedAt | Owner-personalized; public Place does not expose saver identities | Infrastructure only for bookmark relation | Unknown | Owner Remove; cascade when user or Place successfully deleted; deactivation alone retains relation |
| Bin suggestions / photos | Public Add form: label, coordinates, optional image; signed-in contributor ID when present | Name, lat/lon, image URL, date, approval flags/time; nullable user ID; later usage/report counters | Approved bins/photos public; contributor display can be shared; pending not listed on public map | Configured image storage; public image delivery | Unknown | Admin reject/delete removes row; not an established comprehensive media-deletion process |
| Privacy Zone | User chooses precise center and radius in authenticated settings | One user-linked center/radius row | Raw settings owner-only; affects two Community-map walk layers, not all social features | Infrastructure; browser geolocation when chosen | Unknown | Disabling deletes zone row, not historical WalkPoints; successful account deletion cascades |
| Nearby discovery preference | User enables new-bin notifications for chosen center/radius | User, center/radius, enabled flag/time | Owner-only setting; used to generate private in-app notifications | Infrastructure | Unknown | Disabling deletes preference; existing notifications are not shown to be purged by disabling |
| Community / statistics | WalkPoints, completed walks, selected visible dog cities and explicit park visits | Mostly computed projections over retained records; social reactions/comments persist separately | Anonymous aggregate map and city rankings; signed-in Community feed; protections differ by layer | Browser map tiles; infrastructure | Source retention unknown; query freshness windows are not deletion | Depends on source/category; Privacy Zone is not a global opt-out |
| Social / gamification / notifications | Friend requests, playdate invitations/interests, comments/reactions, XP/achievements, in-app notifications | User/dog/content IDs, text, statuses, scores, timestamps/read flags | Friends to participants; public open-playdate API includes dog/name/location label/time/note; public rankings; completed-walk social content to signed-in users | Transactional email is separate; no Web Push integration found | Unknown; read/closed/expired presentation does not imply row deletion | Individual status changes/reaction toggles; many account cascades, but friendships restrict account deletion |
| Public Places / Featured | Admin-curated business/destination records | Category/name/coordinates, address/phone/website/hours/description/images, amenities, featured dates, internal provenance | Supported active public records; verification/provenance contacts excluded from public projections | Cloudinary logos; configured/external image URLs and clicked external websites | Unknown | Admin lifecycle controls; no billing/payment account/data model found |
| Municipal import | Admin uploads CSV from selected DataSource; maps infrastructure fields | Temporary raw parsed rows, then mapped preview; confirmed rows persist label (possibly name + address), coordinates, source relation, approval/dates, no contributor identity | Confirmed infrastructure becomes public; preview Admin-only | App infrastructure; no import-time third-party API call found | Preview TTL 30 minutes; minute pruning; earlier cancellation/restart/mapping; confirmed bins unknown | Cancel clears session; mapping discards unused columns; successful import clears row selection/preview payload; imported bins Admin-managed |
| DataSource operational contacts | Admin-entered municipality/utility/partner contact details and provenance | Name/type/website, contact name/email, notes, data date, created/updated | Admin-only contact metadata, not public Place/bin payloads | Infrastructure; real correspondence outside app requires owner facts | Unknown | Admin delete; bin/Place source FKs become null. Contact data may identify natural persons |
| Cookies / browser state | Login/CSRF/messages, preferences, planner start, install/intro dismissal, proximity UI | Cookies/browser storage; details below | Browser-specific, not public app listings | Authentication redirects/resources separately below | Some framework lifetimes; most preferences no app expiry | Cookie sign-out/consumption; browser controls; preference updates; not comprehensive account-delete cleanup |
| Operational logs / backups | Application/framework/hosting operations and errors | Logs can contain IDs, media URLs and errors; actual platform retention/backup settings unknown | Operational access, not app public content | Render and actual configured logging/backup providers need confirmation | Unknown | No repository-wide retention/purge process established |

Model evidence: `DoggyDrop/Models/{ApplicationUser,Dog,Walk,WalkPoint,WalkPhoto,PlannedWalk,PlannedWalkStop,PlannedWalkRoutePoint,DogParkVisit,SavedPlace,TrashBin,PrivacyZone,NearbyDiscoveryPreference,Friendship,PlaydateRequest,PlaydateInterest,WalkComment,WalkReaction,UserNotification,Place,DataSource}.cs`, gamification models, and `Data/ApplicationDbContext.cs`.

## 3. Authentication, cookies and browser storage

### Authentication and Google

`Program.cs` configures ASP.NET Core Identity/EF stores, password login, roles, optional Google OAuth and persistent Data Protection keys in the application database. `RequireConfirmedAccount` is false. Registration sends welcome and confirmation email; forgot-password sends reset instructions through `IEmailSender`. Local login has RememberMe; logout signs out. A local password hash is distinct from a Google password: the Google flow does not obtain a Google password.

Google package `8.0.5` requests its default `openid`, `profile`, `email` scopes; no extra app scopes or offline access request were found. Callback is `/signin-google`. `ExternalLogin.cshtml.cs` reads email/name claims, creates an Identity user (email/username/display name, confirmed flag), and stores provider/login key via AddLoginAsync. It does not copy a Google profile photo into ProfileImageUrl.

**SaveTokens is true.** OAuth stores the access token and any returned refresh token/token expiry in authentication properties for the external ticket; it is incorrect to say “no tokens are stored.” No application call to UpdateExternalAuthenticationTokensAsync/SetAuthenticationTokenAsync for Google token database persistence was found. The custom callback does not explicitly carry those properties into the main application sign-in. This distinguishes temporary external-cookie token storage from an established long-term Google-token database store. Do not promise that Google returns only the fields the app chooses to persist, or that a refresh token can never be returned.

Sources: `Program.cs`; `Areas/Identity/Pages/Account/{Register,Login,Logout,ExternalLogin,ForgotPassword,ResetPassword}.cshtml.cs`; `Areas/Identity/Pages/Account/Manage/Index.cshtml.cs`; [GoogleOptions 8.0.5](https://raw.githubusercontent.com/dotnet/aspnetcore/v8.0.5/src/Security/Authentication/Google/src/GoogleOptions.cs); [OAuthHandler](https://raw.githubusercontent.com/dotnet/aspnetcore/v8.0.11/src/Security/Authentication/OAuth/src/OAuthHandler.cs). The local isolated options probe corroborated the configured scope/token behavior.

### Cookie inventory

The isolated probe registered the same Identity defaults and Google option behavior without running application startup. These are code/runtime defaults, not an inspection of production cookies. A ticket validity is not necessarily a browser cookie's persistence period.

| Cookie | Purpose / transmission | Proven lifetime/behavior |
|---|---|---|
| `.AspNetCore.Identity.Application` | Signed-in authentication; sent to app; HttpOnly and framework essential flag | Default ticket 14 days with sliding renewal. Password RememberMe can make it persistent; Google/custom registration use nonpersistent sign-in. No universal fixed “14 days then erased” claim |
| `Identity.External` | Temporary external-login claims/properties, including saved OAuth tokens; sent to app; HttpOnly | 5-minute ticket default; nonpersistent browser cookie; login GET clears external scheme |
| `Identity.TwoFactorUserId` | Temporary 2FA login state, if used; sent to app | 5-minute ticket default |
| `Identity.TwoFactorRememberMe` | Remembered 2FA browser, if used; sent to app | Default ticket 14 days, sliding; actual issuance depends on user flow |
| `.AspNetCore.Correlation.*` | OAuth request/callback correlation, if Google used; sent to callback host | Remote authentication timeout default 15 minutes; successful validation consumes correlation cookie |
| `.AspNetCore.Antiforgery.*` | CSRF protection for forms; cookie + request token; sent to app; HttpOnly/essential | No fixed MaxAge configured; browser-session cookie. Antiforgery request token is separate from the cookie |
| `.AspNetCore.Mvc.CookieTempDataProvider` | Protected temporary redirect/status/reward/error messages; sent to app; HttpOnly | Default cookie TempData, no fixed MaxAge; read/consumption normally clears messages. Framework IsEssential defaults false, which is not itself a legal classification |

No AddSession/UseSession, custom consent-cookie implementation or tracking-consent UI was found. CookiePolicy sets minimum SameSite Lax and Secure Always outside Development. The app's functional cookies are not evidence of analytics cookies. No new banner is justified merely by wanting a legal-looking UI; exemption/consent analysis for optional storage and external resources remains an owner/legal decision. No banner was added.

### Browser storage inventory

These are **not cookies** and are not automatically attached to HTTP requests. No IndexedDB usage, app service-worker registration, PushManager subscription or persistent frontend route-point database was found in the first-party frontend.

| Store / exact key(s) | Content and purpose | App expiry / server interaction |
|---|---|---|
| localStorage `doggydrop.homeIntroDismissed.v1` | First-run intro dismissal boolean | No app expiry; read locally; not a server profile |
| localStorage `pwaPromptShown` | Install-tip already shown | No app expiry; local only |
| localStorage `doggydrop.map.showBins`, `showParks`, `showWater`, `showCafes`, `showDogs` (same prefix) | Map display toggles | No app expiry; local preference. Enabling a layer may cause its normal API/resource requests |
| localStorage `doggydrop.notifications.walkReminder`, `nearbyDog`, `badgeEarned` (same prefix) | Settings UI booleans | No app expiry; no server persistence/enforcement of these browser toggles was found. Do not portray them as controlling all server notifications |
| localStorage `doggydrop.walk.autoCompleteStops`, `doggydrop.walk.vibrateOnStop` | Active-walk proximity behavior | No app expiry; automatic completion can invoke normal authenticated stop-completion request |
| localStorage `doggydropPlannerLocation` | Precise latitude/longitude and savedAt used to prefill planner | Ignored after 12 hours; **not automatically removed** at that time. Planner submission sends location to server/routing services |
| sessionStorage `doggydropPlannerGpsTried` | Avoid repeated automatic planner GPS prompt in tab | Session/tab state; no explicit timed expiry |
| sessionStorage `doggydrop.walk.binAlerts.{walkId}` | Per-walk proximity alert deduplication state | Session/tab state; no persistent route track. Not automatically sent as storage |

Home/Active tracking state and request queues are in memory; persisted walk recovery comes from server WalkPoints. Browser session restoration and ordinary HTTP caches are separate browser behavior, not controller-approved retention policies. Signing out does not explicitly clear all the listed browser preferences/planner location.

Evidence: all first-party `wwwroot/js` and Razor storage searches; `home-intro.js`; `Views/Home/Settings.cshtml`; `Views/Walks/{Active,Planner}.cshtml`; `Views/Map/Index.cshtml`; `Views/Shared/_Layout.cshtml`; `wwwroot/manifest.json`. `walk-share.js` creates a local canvas/PNG and uses explicit browser share/download actions; it does not automatically post stories to social providers.

## 4. Location and public/private boundaries

- **Active/Home recorder:** browser watchPosition is used during the active recording flow, with location permission and lifecycle/recovery handling. AddPoint receives exact coordinates, timestamp and accuracy; stores accepted lat/lon/time points against the owned walk. Distance is calculated and persisted; duration is derived from start/end times. No standalone Walk “memory note” column exists; captions/comments and derived memory presentation are different. Browser operation is not a guarantee of continuous background GPS.
- **Discovery:** `place-discovery.js` uses an optional one-shot getCurrentPosition and in-memory client-side distance sorting; no fetch/storage of that location in this feature.
- **Nearest bin:** Home sends coordinates in the GET query to `/Map/GetBestBin`; related Nearby APIs can receive coordinates too. The read-only calculation does not write a visitor-location row. Coordinates in request URLs can still reach infrastructure logs; “never sent to a server” is false.
- **Navigation:** Home watches position and POSTs origin/destination coordinates to the same-origin antiforgery-protected `/api/walking-route` endpoint. The server alone calls OpenRouteService `foot-walking`; no OSRM/driving fallback remains. The ORS key stays server-side. Missing/error/quota responses produce an explicitly labelled approximate direct line, not a walking route. Map tiles reveal requested map areas to tile services; a panned map need not equal the user's location. Navigation alone is not an AddPoint write unless the active-walk recorder is also operating.
- **Planner:** obtains current location (including an automatic attempt when required inputs are absent), keeps a browser prefill, and passes a submitted origin to the server and routing providers. Saved plans persist owned stops/options and successful routed geometry; new unverified generated geometry is not saved as a route overlay. Existing historical plans are not rewritten. Approximate previews are explicitly labelled and have no walking ETA. A planner location is not covered by Discovery's client-only statement.
- **Other GPS uses:** Add-bin form can populate bin coordinates; Settings can populate Privacy Zone/Nearby center (saved only through settings action); Notifications weather sends coordinates directly to Open-Meteo. There is no single uniform location-retention rule across these features.
- **Privacy Zone:** authenticated owner settings store exact center and one of the supported radii. Disabling deletes that setting. Current active-presence and completed-route hotspot queries exclude owners with any zone configured. They do not remove their stored WalkPoints. The route-sharing sanitization helper is preparatory; non-owner Walk Details currently strips all route/plan geometry, rather than exposing a sanitized route.
- **Community map:** active presence uses fresh latest positions per owner and fixed coarse cells with multiple contributors; completed-route cells also require multiple owners. These protections must not be generalized to park visit counts or visible-dog density, which have different rules and can report a single contribution. No account names/IDs or individual track are returned in the walk-hotspot cell payload.
- **Nearby dogs:** Visible is anonymously accessible, FriendsOnly requires an accepted friendship, default is Invisible. Current dog-location writes use a selected predefined approximate city, not live WalkPoint GPS. The endpoint returns the stored location, name, breed, size, character, image/icon and owner display name. Do not promise all dog profile data is private, or claim this is live individual walk GPS.
- **Social and rankings:** signed-in Community/Walk Details expose completed-walk summaries, photos and captions; route geometry is owner-only. Public open playdates expose user-entered location labels/time/notes and dog/profile labels. Public local rankings derive city association from a walk's first point and return user identifiers and labels; Privacy Zone is not consulted there. If DisplayName is missing, the label can be the account email. The same fallback appears on some signed-in Community/friends surfaces. **Do not state that account email is always private or that Privacy Zone removes all public activity.**
- **Saved Places:** owner-filtered relation and remove/save actions; public Place metadata does not disclose who saved it.

Evidence: `WalksController.AddPoint/Details/Planner/SavePlan/DeletePlan`, `MapController.GetBestBin/Add/ParkVisit`, `HomeController.SavePrivacyZone/SaveNearbyDiscovery/Community`, `Api/{CommunityMapApi,DogsApi,WalksApi,PlaydatesApi,LeaderboardsApi,FriendsApi,TrashBinsApi}Controller.cs`, `LocalLeaderboardService`, `WalkRoutePrivacyService`, `SavedPlacesController`, and the corresponding views/scripts. No personal production coordinates were used in this audit.

## 5. External services, media and operational data

Technical communication is not a GDPR processor/controller classification. All regions, contractual roles, DPAs and transfer safeguards below remain **NEEDS OWNER INPUT** unless separately confirmed. No provider was contacted using production credentials or application data.

| Service/capability | Proven communication and data involved | Production qualification |
|---|---|---|
| Render / PostgreSQL | Application HTTP traffic and stored app records; Npgsql; DB-persisted Data Protection keys | Web Render and PostgreSQL confirmed by owner; DB operator/regions/backups/log retention not established |
| Google authentication | Browser redirect and server token/user-info exchange; scopes/claims/tokens as above | Available per owner; legal/provider-account arrangements unverified |
| Google Fonts | Both app and project layouts request Nunito CSS from fonts.googleapis.com and fonts from fonts.gstatic.com | Browser makes third-party requests, including network/browser request metadata; no “all fonts self-hosted” claim |
| Google static asset | Login/Register request Google logo SVG from www.gstatic.com | Static image request, not evidence of Firebase analytics or Google Maps API |
| Stadia Maps (optional) / OpenStreetMap | Browser raster tiles via tiles-eu.stadiamaps.com or tile.openstreetmap.org; tile area/indexes, IP/browser metadata and origin referrer; no DoggyDrop IDs, route geometry or user fields in query parameters | Epic 19.4 centralizes all Leaflet surfaces. Default OSM; owner must explicitly enable Stadia after plan/domain setup. CARTO is no longer requested. Three Stadia tile failures cause one OSM fallback. No proxy/offline caching; browser HTTP caching only. EU endpoint routing does not establish GDPR/legal-transfer compliance; production activation and contractual facts NEED OWNER INPUT. See map-basemap.md. |
| unpkg / jsDelivr | Browser Leaflet JS/CSS and Bootstrap Icons/font delivery | Third-party resource requests even without analytics |
| OSRM (removed in Epic 19.5) | No current Home/Planner routing requests; old public fallback removed | Historical saved plans may still contain old geometry; they are not re-certified |
| Overpass | Planner server posts coordinate/radius infrastructure query to overpass-api.de | Server-side flow; no direct visitor browser IP is added by this code |
| OpenRouteService | Home/Planner server POST ordered coordinates and fixed options to api.openrouteservice.org/v2/directions/foot-walking/geojson. No UserId/email/dog/WalkId/Place name/Privacy Zone or forwarded browser IP. Server IP and ORS account authorization necessarily reach ORS. Home app POST includes normal same-origin cookie/antiforgery context, not forwarded to ORS. No persistent navigation cache; shared in-memory request budget and per-IP endpoint limiter. Provider HTTP logging disabled; only status/error type logged. | Production key/account, provider retention/roles/regions/transfers remain NEEDS OWNER INPUT. Missing key fails safely. No production key or route inspected; see walking-routing.md. Existing Planner/nearest GET origin parameters can still reach infrastructure logs. |
| Open-Meteo | Notifications page sends browser geolocation to api.open-meteo.com for weather | Direct browser flow, not app WalkPoint persistence |
| Cloudinary | Upload API and res.cloudinary.com image delivery; Place-logo service separately uses Cloudinary when configured | R2 configuration takes priority for general uploads, but does not eliminate legacy Cloudinary URLs or the separate logo flow |
| Cloudflare R2 | S3-compatible server uploads and browser delivery via configured public base/domain | Active backend, actual region, custom domain and existing objects unverified |
| Local media | App writes files below wwwroot/uploads in fallback paths; static-file delivery | Code supports local fallback even though startup messaging can suggest uploads disabled. Persistent disk/backup behavior unknown |
| SMTP provider | MailKit STARTTLS sends recipient/address/content for welcome/confirmation/reset and explicit test actions | Configurable provider; actual operator/region/logging/delivery unverified |
| Other configured image/website URLs | Admin Place ImageUrl and external website links may point at other hosts; image tags can request them directly | Dataset-dependent host list unverified; no production records inspected |

No current Google Maps/Places/Directions/Geocoding API integration, GA4/gtag/GTM, Meta Pixel, Hotjar, Clarity, Sentry, Mixpanel, PostHog or Amplitude SDK, social/video iframe embed or advertising tracker was found in first-party code and package references. The application's `api/analytics` is its own aggregate bins/walks/community statistics, not a browser-tracking SDK. This is a repository finding, not proof against deployment-injected scripts or every third-party response. No marketing-email or browser-push campaign mechanism was found. In-app stored notifications and computed reminders do exist; some are periodically fetched by the browser.

Evidence: `Program.cs`, `DoggyDrop.csproj`, `Dockerfile`, `Services/{OsmWalkPlannerService,CloudinaryService,CloudflareR2StorageService,MissingCloudinaryService,PlaceLogoStorage,EmailSender,NotificationService,NearbyDiscoveryService}.cs`, `Views/Shared/{_Layout,_ProjectLayout}.cshtml`, `Areas/Identity/Pages/Account/{Login,Register}.cshtml`, `Views/Notifications/Index.cshtml`, map views and `map-basemap.js`.

### Photo and import qualifications

New bin uploads accept JPEG/PNG/WebP with bounded size/dimensions and matching format checks. `ImageOptimizationService` decodes pixels, normalizes orientation, re-encodes WebP and strips original metadata before Cloudinary/R2/local bin upload; failed sanitization does not pass original bytes through. Approved bin photos are shown publicly. Approval is a listing gate, **not private file authorization**: public provider/static asset URLs can exist before approval. Do not promise removal of all metadata from all historical or profile images.

Profile/dog photo Cloudinary upload can send original bytes and original filename before provider processing; local profile fallback can preserve originals. R2 profile optimization has a non-strict fallback. Walk Cloudinary upload sends original bytes with auto-orientation/force-strip transformation; R2/local walk paths sanitize. Thus “no provider ever receives photo metadata” is not supported for every upload category. Bin-photo guarantees must not be extended to other media categories. Image URLs/filenames may themselves be identifying. Source: upload services, `BinPhotoUploadPolicy`, `ImageOptimizationService`, `CloudinaryImageDelivery`.

Municipal CSV is bounded to 5 MiB and held in instance memory for preview. Unmapped raw columns are dropped when mapping succeeds; mapped name/address are combined into the persisted public bin label. They can contain personal text if supplied: the importer does not semantically anonymize them. Raw CSV is not deliberately persisted to disk or logged by importer code; the custom upload form limit keeps accepted input within its memory buffer. Preview expiry is 30 minutes with periodic minute pruning, plus cancellation/restart/earlier payload disposal. This is logical lifecycle behavior, not a secure-memory-zeroization guarantee. Successful import writes only mapped bin fields, DataSourceId and approval/dates with UserId null. It does not import an arbitrary personal contact column into the database. `DataSource` contact name/email/notes are separate Admin-managed data and may be personal information about municipal/business contacts. Source: `AdminBinImportController`, `BinImport{Csv,Models,Sessions,Service}`, `AdminDataSourcesController`.

Featured adds dates/flag to a public Place; no card details, billing identities, payment checkout or advertiser account was found. Ordinary ranking, nearest-bin calculations, gamification and rule-based planning are visible; no process making legal/similarly significant decisions about people was identified. This does not assign an Article 22 legal conclusion or a lawful basis.

### Logging and retention limits

AddPoint diagnostic messages contain request ID, Walk ID, outcome/reason, point count and timing; no coordinate/name fields. Home/Active debug panels show counters/status/timing/distance, not a coordinate log stream. Finish diagnostics also log phase/status/timing. **This does not prove that no coordinates can appear anywhere in logs:** nearest-bin URLs and outbound routing URLs contain coordinates, framework/HttpClient logs can include request URLs, and production proxy/log configuration is unverified.

Other code logs include media URLs/status/provider errors (Cloudinary profile upload), object keys/bucket/error context (R2), user IDs in achievement-reconciliation failures, and exception messages in SMTP/planning/provider paths. There is no repository-supported universal claim that logs contain no personal information. No first-party IP-address field in the examined domain models or explicit app request-IP log was found; normal hosting/network processing of visitor IP and platform logging still require operational confirmation. No log/backup purge timetable is established. Persistent Data Protection key storage is not proof that the whole database or all logs are encrypted at rest.

## 6. Account deletion, export and actual erasure

Do not conclude there is no self-service account path just because its source files are not scaffolded in this repository. The referenced Identity UI 8.0.11 supplies `/Identity/Account/Manage/PersonalData`, `DeletePersonalData` and `DownloadPersonalData`; local action-descriptor inspection confirmed all three. The custom Manage navigation links PersonalData.

- **Export is partial.** Framework export includes annotated Identity user properties, external-login provider keys and authenticator key. Runtime reflection found `Id`, `UserName`, `Email`, `EmailConfirmed`, `PhoneNumber`, `PhoneNumberConfirmed`, `TwoFactorEnabled`. Custom DisplayName/ProfileImageUrl are not annotated; dog profiles, walks/points, saved Places, contributions, settings and media bytes are not exported by that default page. A complete data-access response requires an additional owner-approved process.
- **Deletion is not a reliable complete workflow.** Default UI checks a local password when present, calls UserManager.DeleteAsync, then signs out. There is no app-specific dependent-record/media cleanup step in that handler.
- Model cascades cover Identity claims/logins/roles/tokens, dogs, walks/points, saved Places, plans/dependent stops/route points, Privacy Zone, Nearby preference, notifications, comments/reactions/photos, visits, playdates, XP/streak/achievement records when the user deletion can successfully execute. They are database-row relationships, not proof of external media erasure.
- Friendship requester/addressee relationships are Restrict. Bin UserId is ClientSetNull, **not database ON DELETE SET NULL**. The default user-only deletion context does not load/anonymize bin contributions. Both can block deleting a referenced user.
- The isolated in-memory SQLite probe, with foreign keys enforced, confirmed: user with dog/walk/point deletes with those dependent rows; user with a contributed bin is rejected by a foreign-key constraint; user with a friendship is likewise rejected. These are model/store-level checks, not a production PostgreSQL or full browser account-delete test. Corresponding model delete behaviors were also enumerated.
- No automatic anonymization of bin authors or friendship cleanup was found in the default account-delete path. Do not state that bins survive with the author anonymized, or that deletion always succeeds.
- Walk DeletePhoto removes the row, leaving the hosted/static file; replacing user/dog photos does not establish old asset deletion. Media rotation/logo cleanup routines exist for their specific workflows but are not a general account-erasure service. R2's public cache header can allow long-lived cached copies; a cache TTL is not a retention schedule.
- No dedicated self-service whole-walk or dog deletion action was found. Saved Place removal, Privacy Zone disable, Nearby disable, plan deletion and photo-row deletion are narrower actions. Browser preferences, operational logs, backups and external provider copies are outside EF cascades.

Sources: `Data/ApplicationDbContext.cs`, model/migration relationship definitions, `Areas/Identity/Pages/Account/Manage/_ManageNav.cshtml`, `WalksController.DeletePhoto/DeletePlan`, `HomeController.UpdateProfile`, `DogsController.Edit/UpdatePhoto`, [Identity deletion source](https://raw.githubusercontent.com/dotnet/aspnetcore/v8.0.11/src/Identity/UI/src/Areas/Identity/Pages/V5/Account/Manage/DeletePersonalData.cshtml.cs), [Identity export source](https://raw.githubusercontent.com/dotnet/aspnetcore/v8.0.11/src/Identity/UI/src/Areas/Identity/Pages/V5/Account/Manage/DownloadPersonalData.cshtml.cs).

## 7. Retention periods actually supported

- Import preview: 30-minute logical expiry, pruning every minute/on operations; earlier release on mapping, completed import, cancel or process restart depending on payload. Confirmed bins are not subject to that TTL.
- Planner localStorage coordinates: ignored when savedAt is over 12 hours old; bytes not cleared by this age check.
- Auth/correlation/2FA ticket defaults: section 3; not a general account-retention policy and not necessarily browser-session persistence.
- WalkStaleness: after more than 24 hours inactivity, an active walk can be closed as Interrupted on relevant application flow; history/points remain. This is neither scheduled erasure nor a 24-hour data-retention policy.
- Community/map/history/notification duplicate-detection freshness windows only bound what is displayed or generated. They do not delete the underlying rows. Mark-as-read and closing a playdate are not erasure.
- No approved/code-enforced general retention or inactive-account purge was established for account/profile, GPS/history, media, bookmarks, contributions, social/notification/gamification, provenance contacts, logs, backups or rights/outreach correspondence. Do not substitute “until account deletion” for the missing schedule.

## 8. Purpose-by-purpose owner/legal decisions

No GDPR basis is selected in this audit. Each row needs the controller's chosen basis, justification/scope, any needed consent/objection mechanism, retention and recipients/transfer framing. A “contract” or “legitimate interests” label must not be generated just because a feature exists.

| Actual purpose | Processing to decide upon |
|---|---|
| Account access, recovery and security | Identity records, login cookies, Google claims/tokens, confirmation/reset email, optional phone/2FA |
| Personal dog/activity service | Dog/profile records, walk history/precise routes, planning, activity insights and bookmarks |
| Optional device location features | Recorder, one-shot Discovery, nearest bin, navigation, planner, settings centers and weather; browser permission is only the technical gate |
| Public infrastructure contributions | Anonymous/account-linked bins/photos and contributor attribution; distinguish listing permission from privacy basis |
| Community/social publication | Completed summaries/photos/comments, public dog/playdate visibility, aggregated map contributions and named public city rankings/email fallback |
| Gamification and in-app reminders | XP/streaks/achievements, deduplication history, notifications and selected-area bin announcements |
| Hosting, diagnostics and external resources | Network requests, maps/routing/fonts/CDNs, stored media, operational logs/backups; storage exemption/consent assessment where applicable |
| Municipal/business sourcing and support | Named contacts/emails/free-text provenance notes, dataset reuse and correspondence; information collected from others may require separate notice analysis |
| Rights handling / applicable duties | Identity verification, response/export/erasure work and records of requests; process and retention still to be approved |
| Any off-app promotional reuse | Existing Terms mention it, but actual operations, basis and permission process have not been established; do not silently import that promise into Privacy |

A rights section (access, correction, erasure, restriction, portability/objection/withdrawal where applicable, complaint) should be drafted only with the approved framing and honest process. GDPR Articles 13/14 require transparency about purposes/bases, recipients/transfers and retention periods or criteria; this is why unknown details cannot simply be replaced with reassuring generic wording. Reference: [official GDPR text](https://eur-lex.europa.eu/eli/reg/2016/679/2016-05-04/eng/pdf). No controller-specific rights limitation or age threshold is inferred here.

For later approved complaint wording, the current official [Informacijski pooblaščenec contact page](https://www.ip-rs.si/o-poobla%C5%A1%C4%8Dencu/osebna-izkaznica) was checked on the audit date: Dunajska cesta 22, 1000 Ljubljana; general contact gp.ip@ip-rs.si. This is the supervisory authority's address, **not DoggyDrop's controller address**. No public rights section or effective date was added yet.

## 9. Verification and task boundary

- Repository/source/package audit covered the requested identity, auth, cookies/storage, analytics, profiles, GPS, sharing, contributions, providers, logs, retention, deletion and export areas. Negative findings mean “not found in this repository,” not production attestation.
- External isolated harness: `C:\Codex\epic221-privacy-audit`. It references the project, clears host configuration, constructs Identity options/action descriptors and uses only synthetic in-memory SQLite databases. It never runs DoggyDrop's normal Program/startup, never starts an HTTP server, never opens the configured production connection and never calls image/SMTP/map providers.
- Confirmed registered default PersonalData pages, Google scopes/SaveTokens, cookie defaults, annotated export fields and user-FK delete behaviors. Three deletion scenarios and one public-leaderboard scenario reproduced the behavior above. The latter returned a synthetic email label even with a Privacy Zone configured. Results: `C:\Codex\epic221-privacy-audit\results.txt`.
- The harness compiled successfully with existing dependency/nullability warnings. This was a targeted audit, not a rerun of the full 837-test suite; no new production implementation or policy-render tests are claimed.
- No application implementation, Privacy, Terms, authentication configuration, schema or migrations were changed. This internal audit document is the only intended repository addition; no external harness/build artifacts are included. No commit, push, deployment, migrations or production-data access were performed.
- Next step is owner/legal decisions and any separately authorized behavior corrections, followed by factual Slovenian policy drafting and rendering/regression verification. **Current verdict: NEEDS OWNER INPUT; public launch and municipal outreach remain blocked.**

## Epic 22.2 — implementation deletion graph (before lifecycle changes)

The preceding sections preserve the original audit evidence. This section records deliberate technical changes; it is not an approved retention policy.

- User → approved TrashBin: explicitly null contributor UserId, preserve public infrastructure and image. User → pending TrashBin: delete submission and consider its managed image for cleanup.
- User → Friendship (RequesterId and AddresseeId): explicitly remove both directions before deleting Identity user; both existing FKs are Restrict.
- User → dogs → dog progression/XP, walks → points/photos/reactions/comments/stop completions: existing database cascades remove private activity. Account-owned walks also cascade independently of dog ownership. Collect all affected photo URLs before deletion.
- User → SavedPlaces, PrivacyZone, NearbyDiscoveryPreference, visits, playdate requests/interests, plans → stops/route points, notifications, user XP/streaks/achievements/founder badges: existing cascades. Public Places and DataSources remain. Other references to deleted plans/stops/bins follow existing SetNull/cascade rules.
- User → Identity claims/logins/tokens/role memberships: cascade. The Identity user/profile is deleted. Shared roles and Data Protection keys remain.
- Other-account automatic social notifications: remove copied actor identity from future messages; neutralize matching legacy generated messages during deletion. Friend-request notifications already contain no actor identity and may remain as generic history. Do not inspect or rewrite arbitrary user-authored text.
- One database transaction contains explicit relationship cleanup and Identity deletion. PostgreSQL user-row lock prevents new dependent FK inserts while collecting/deleting the graph. Rollback must leave the graph intact. Collect managed account/dog/walk/pending-bin media first; cleanup occurs only after successful commit and fresh live-reference checks. External storage failure cannot roll back a committed account deletion.
- No schema or FK changes are planned. No migration, production access, commit, push or deployment is authorized for this task.

## Epic 22.2 — hardening result (2026-09-28)

### FIXED — public identity

Public fallbacks in LocalLeaderboardService, Home/Community, both WalksController label helpers, WalksApiController, FriendsController, FriendsApiController, Walk Details comments and Playdates now use a neutral `Uporabnik` label when DisplayName is missing/blank. Friend sorting no longer uses Email as its fallback. Google account creation no longer derives a display name from the email local part when name claims are absent. Explicit existing DisplayName values are not guessed/reclassified or bulk-rewritten.

Complete application search for `.Email`, `UserName`, `NormalizedEmail`, `NormalizedUserName` was classified as follows (generated/build output excluded):

| Occurrences / surfaces | Classification and outcome |
| --- | --- |
| LocalLeaderboardService; HomeController community label helper; WalksController display/leaderboard helpers; WalksApiController social author helper; FriendsController labels/sort; FriendsApiController Mine/Requests; Walks/Details and Playdates/Index | PUBLIC PRESENTATION — email/username fallback removed; regression requests exercise anonymous leaderboard, friends and walk social JSON. |
| Map/Index and TrashBinsApi contributor names; DogsApi nearby owner names; other achievement/dog/Community projections | PUBLIC PRESENTATION — existing public-name/neutral-label projections; no account email fallback found. |
| HomeController UserProfile lookup and its Email projection; UserProfileViewModel; Home/UserProfile; signed-in Shared/_Layout and _LoginPartial; Map current-user greeting; Identity login/register/external/reset/forgot/manage pages | PRIVATE ACCOUNT UI / AUTH — current user or account-management flow, not another user's public display. Account identifier semantics preserved. |
| Program account seeding; Map/Manage contributor Email (action has Admin role authorization); DataSource contact view models/Admin views | ADMIN — not public projections; unchanged. No seed credential is reproduced here. |
| Register notification and confirmation email; ForgotPassword delivery; EmailSender/EmailSettings SMTP addressing | EMAIL DELIVERY — unchanged. |
| Project/_Contact and PublicContact model | PUBLIC PRESENTATION — intentionally configured public project mailbox, not ApplicationUser email; unchanged. |
| New PersonalDataExport account projection and allowlisted profile claims | PRIVATE ACCOUNT UI — authenticated owner only; never used as public presentation. |
| Identity migration/designer/snapshot normalized username/email columns/indexes | PRIVATE ACCOUNT/AUTH SCHEMA — generated storage definitions, not presentation; unchanged. |

No public projection of a complete Identity entity was introduced. Tests reject both a synthetic email and its local part, including a leaderboard user with Privacy Zone enabled.

### FIXED — account, walk and media lifecycle

Custom Identity Manage/DeletePersonalData now calls AccountDataDeletion. Existing authenticated POST and antiforgery protection remain. Password-bearing accounts must supply their current password; all accounts must explicitly confirm permanent deletion. External-only accounts retain the existing authenticated-session deletion flow, with the additional confirmation checkbox. Failure messages are Slovenian and do not disclose exceptions.

The graph above is implemented without a schema change: approved bins retain public content/image with UserId NULL; pending submissions are deleted. Both friendship directions are removed. Database cascades remove the account, dogs, owned walks/raw points/photos, plans/stops/route points, SavedPlace relations, privacy/discovery settings, visits, playdates/interests, comments/reactions, notifications, dog/user XP/progression/streaks/achievements/founder badges, Identity claims/logins/tokens/role memberships. Shared Places, DataSources, Identity roles and Data Protection keys are not deleted. There is no stored anonymous aggregate requiring historical de-aggregation; current map aggregates are recomputed from the remaining rows.

PostgreSQL user, dog and affected walk row locks protect graph collection from new dependent inserts. Explicit unlink/delete operations and UserManager.DeleteAsync run in one database transaction. Failed Identity deletion or database exceptions roll back; failed tracked entries are cleared before rendering an error. No media cleanup runs on rollback. Full-graph tests also preserve another user's dog, walk, GPS, SavedPlace and zone.

Walk photo deletion now goes through WalkDataDeletion; whole-walk deletion has a safe service entry point collecting photos before the cascade. No new whole-walk endpoint/UI was added. There is no other existing user Walk/Dog delete endpoint; account deletion collects photos cascaded through owned dogs/walks and directly user-owned photos. The failed-new-Google-account cleanup remains the separate existing pre-content Identity creation rollback.

Managed account/dog/walk/pending-bin assets are considered only after a successful database commit. UserMediaAssets follows the existing hardened bin-asset validation approach: configured Cloudinary cloud or R2 base only, allowlisted uploader namespaces/filename formats, strict HTTPS remote URLs, local GUID upload paths and reparse-point checks. It covers profile/walk/bin uploads and known R2 migration/optimization folders. Arbitrary external, malformed or unrecognized URLs are retained, never fetched/deleted. Fresh reference checks include user profiles, dogs, walk photos, bins and Place images/logos, conservatively including transformed Cloudinary aliases and delivery query strings. Same assets are deduplicated. Cleanup is best effort: errors log only backend category, without URLs, provider exception details, profile data or GPS. Provider failure does not undo database deletion. No durable retry queue/retention deadline or provider backup purge is claimed; an interrupted/failed or unrecognized cleanup can leave an orphan. Live Cloudinary/R2 were not contacted.

### FIXED / CURRENT BEHAVIOR — notifications

There is no actor FK in existing notifications, so historical names cannot be attributed reliably after renames. Future automatically generated WalkReaction, WalkComment, PlaydateInvite and PlaydateInterest bodies are neutral and contain no copied actor/dog/location identity. Export neutralizes those same legacy generated bodies. Account deletion normalizes **all existing messages in those four generated classes**, including other recipients' messages, so renamed historical actors are not missed; event type/title/link/read state/timestamps remain. This is intentionally broader than guessing actor names, and avoids a migration. Other notification types and user-authored comments are not rewritten; the deleted user's comments are removed by cascade. Generic friend-request/acceptance history in other accounts may remain without an actor identifier.

### FIXED — personal-data export

The existing Identity Manage/PersonalData and POST DownloadPersonalData routes now provide one owner-only UTF-8 JSON download. No user-ID input selects the export. GET download is unavailable; anonymous access and missing antiforgery tokens are rejected. Headers explicitly use private/no-store/no-cache, JSON content type, a constant attachment filename and nosniff.

Explicit projections include account/profile, safe profile claims, external login provider identifiers (not tokens), roles, dogs, walk summaries and all owned GPS points, photos as URLs/metadata, SavedPlace relations plus minimal eligible public Place references, zones/discovery settings, bin contribution status, friendship identifiers, notifications, playdates/interests, reactions/comments, park visits, plans/stops/planned route points/completions, achievements/founder badges, user/dog XP/progression and streaks. Other users' profiles/emails/private activities and DataSource Admin contacts are not traversed. PasswordHash, SecurityStamp, authenticator/recovery secrets, arbitrary claims, Identity token records, Data Protection keys and provider credentials are excluded by construction. Previously received generated notifications are neutralized as above, rather than exporting copied private sender identities.

A consistent database snapshot and one projected query per collection avoid per-walk N+1 queries. JSON uses bounded batches of up to 256 records and asynchronous response writes, not a complete in-memory route array. There is no row cap/truncation. Tests include 1,201 owned GPS points and another user's excluded route. Binary media and provider-side logs/backups are not bundled. Browser-local caches/unsent recordings are not database records and are not claimed to be exported.

Google authentication is unchanged except the display-name fallback. SaveTokens stores tokens in the temporary Identity external authentication ticket (default five-minute ticket lifetime established by the audit). No repository call persists those tokens via UpdateExternalAuthenticationTokensAsync/SetAuthenticationTokenAsync. The new export never reads external tickets or user token values.

### FIXED — factual copy; CURRENT BEHAVIOR — sharing

Settings explicitly describes the Privacy Zone as excluding the user's walks from walking layers on the Community map, including historical walks. It does not hide completed summaries/photos, leaderboards, separate dog-density/park-visit layers or alter stored GPS. The non-owner Details route still omits exact route geometry; owner GPS and its recording/acceptance/distance logic are unchanged.

Terms received factual corrections only: email/password versus Google login, actual application data categories, existing social visibility, external services, and honest deletion/export limits. Existing promotional-photo/copyright and other legal clauses were not rewritten; their approval remains unresolved. Public Privacy remains the original placeholder.

**Remaining MEDIUM: default social sharing.** Finishing a walk currently makes its summaries/photos available to other authenticated users without an explicit sharing choice. Anonymous local leaderboards can also show linked dog/activity labels and ranked photo URLs. Privacy Zone does not suppress these surfaces. Photo API responses can include stop names; no raw GPS route was newly exposed by this Epic. This existing behavior was audited and reported, not silently redesigned.

Smallest privacy-preserving follow-up proposal: make non-owner walk summaries/photos and person-linked walk/photo leaderboard entries unavailable until the user deliberately shares them; leave public bin infrastructure and private recording intact. An explicit opt-in visibility field would require a separately reviewed schema decision/migration. A temporary owner-only restriction across MVC, APIs, Community and leaderboards could close the exposure without a migration. Owner product decision is required before choosing that broader change. This finding prevents a SAFE TO COMMIT verdict in this task.

### OWNER DECISION STILL REQUIRED

- Official controller/business postal address (not the earlier placeholder).
- Approved legal bases per processing purpose, retention rules and deletion/backup operations, child/age policy, external-provider production identities/regions and processor/transfer arrangements.
- Explicit activity/photo sharing design and treatment of existing shared content.
- Promotional reuse/copyright and other legal Terms clauses; completed, approved Privacy content.
- Production active storage credentials/backends and deletion operations must be operationally verified by the owner; repository-supported providers are not evidence of active production configuration.

No retention period, age threshold, legal basis, DPA/SCC, region, backup-deletion guarantee or final Privacy policy was invented. Public launch and municipal outreach remain blocked.

### Epic 22.2 verification and final verdict

- `dotnet build -p:RazorCompileOnBuild=true`: PASS. Existing MailKit/MimeKit advisory and nullable-reference warnings remain; no new compilation warnings/errors.
- `dotnet test`: **882/882 PASS**, 0 failed/skipped (45 additional permanent regression cases versus the 837-test baseline).
- Node: **82/82 PASS**, including Home/Active inline-script syntax checks. Standalone JavaScript syntax: **12/12 PASS**. No JavaScript implementation changes.
- Isolated PostgreSQL 17: **5/5 PASS** for full graph, rollback after injected database failure, complete owned export, walk/photo lifecycle, and managed/shared-media cleanup. Same assertions as permanent lifecycle tests, invoked by the external harness `C:\Codex\epic222-privacy-hardening`; synthetic disposable databases only, no migrations. Temporary server shut down successfully.
- HTTP regressions cover authentication, antiforgery, owner-only export despite supplied user-ID parameters, parsed download/headers, password and external-account confirmation, public retained-bin presentation, friend-list cleanup, photo-delete ownership, anonymous identity fallback, social DTOs, Settings and Terms rendering. No live storage, SMTP, map-provider or production requests.
- `git diff --check`: PASS; new-file trailing-whitespace checks: PASS. Privacy, appsettings, model, DbContext and migration/snapshot paths have no diff. GPS recording/distance/threshold algorithms and auth-provider configuration are unchanged.
- Final scope: **27 repository files (13 modified + 14 untracked/new, including the preserved audit document)**. External harness/results remain outside the repository. Local HEAD remains `760de2fa58091c0bb7cfd9fd07d38f5306421d6f`. No staging, commit, push, deploy, manual migration or production access.
- Strict code verdict: **BLOCKER 0 / HIGH 0 / MEDIUM 1 (existing default social sharing, reported above). NOT SAFE TO COMMIT.** The requested concrete lifecycle/identity/export fixes pass their checks; resolving the broader sharing behavior still requires the separately described product choice. Public launch and municipal outreach remain blocked by this and unresolved Privacy/legal/operational decisions.

## Epic 22.2 follow-up — owner-only walk access (2026-09-28)

This section supersedes the preceding sharing finding and code verdict, preserving the earlier audit as history. The owner explicitly chose V1 owner-only walk access, including for friends, without building a sharing model. The existing 27-file hardening tree and its lifecycle/identity/export fixes are preserved.

### Previous exposure and new rule

Previously, completion made a walk's summary, Memory presentation and photos available to other signed-in users. Community projected individual and friend walk feeds and photo cards. Anonymous local leaderboards projected photo URLs and identifiable dog activity. Friends and suggestions also exposed another person's recent walk distance. These surfaces did not require an explicit content-sharing choice.

Now private Walk data is authorized by Walk.OwnerId. Completion, friendship and the presence or absence of a Privacy Zone do not grant access. Authenticated non-owners receive the same 404 as an unknown ID; anonymous requests encounter the existing authentication challenge. Owners retain history, Details/Memory, points/routes/plans/stops, photo metadata and delivery URLs, statistics, and their existing explicitly requested downloadable share assets. The Share alias is also owner-checked; copying a Details URL grants no access to another account. No public sharing route, selector, storage table or schema field was created.

### Routes and projections reviewed

| Surface | Current boundary / result |
|---|---|
| Walks/Details/{id}, its embedded Memory and optional share presentation | Owner-only query before returning any model. Non-owner model redaction has been replaced by denial of the entire resource. |
| Walks/Share/{id}, ShareCard/{id} | Owner-checked alias and existing owner-only downloadable asset. No public-access token or sharing permission is created. |
| api/walks/{id}/social and /photos | Owner-only, including all photo references, captions, timestamps and stop names. |
| Walks/ToggleLike, AddComment, TogglePhotoReaction; api/walks/{id}/like, /comments and api/walks/photos/{photoId}/like | Owner checks prevent cross-user mutations and response-based disclosure. Existing MVC antiforgery remains enforced. |
| Walks/DeletePhoto and WalkDataDeletion | Existing creator check now also requires ownership of the parent walk. Database deletion and post-commit managed media cleanup remain intact. |
| Walks Index, Active, Interrupted, FinishStatus, Plan, Planner; api/walks stats, recent, plans and individual plan | Existing owner boundaries retained. The secondary weekly walk/dog rankings in Index are now also limited to the current owner's walks, with matching labels. |
| Walk creation/planning, AddPhoto, AddPoint, ToggleStop, Finish | Existing ownership gates retained. GPS acceptance, distance, coalescing, timing and Finish algorithms are unchanged. |
| Dogs Index, Details, Adventures; api/activity/summary | Owner-scoped dogs and walk/photo history retained. Another user's dog selection is denied. |
| Home/UserProfile | Current signed-in account lookup, not a selectable public profile. Supplying another user ID does not expose their content. |
| Friends page and suggestions; friends APIs | Deliberate non-walk profile fields (name, avatar, dog count and relationship metadata) remain. Walk distances and suggestion ranking by private distance removed. Friendship grants no walk access. |
| Home/Community | Only weekly aggregate counts/distance, safe local rankings, approved-bin gallery and existing Playdate navigation remain. No private feeds in its model or HTML; obsolete sharing prompts/cards removed. |
| api/leaderboards/local | Distance ranks contain neutral labels and aggregate score/count only, without user/dog IDs, photo URLs, route, walk links or exact walk timestamps. Photo/dog-activity collections are empty for DTO compatibility; their UI sections are removed. Public bin/park contribution rankings remain. |
| api/community-map/heatmap | Existing coarse walking cells and minimum two distinct contributors preserved, with Privacy Zone exclusion. Separate opt-in dog-density and public park-visit layers unchanged. No per-walk geometry or identity is serialized. |
| City/Dashboard and api/analytics/summary | Walk data reduced to aggregate totals/day buckets, without private walk cards, identifiers, photos or routes. |
| Notifications and notification APIs | Recipient-scoped history and neutral generated messages retained. Historical Walk links still pass through owner authorization; a link is not a grant of access. New cross-user walk reactions/comments are denied. |
| Admin/MediaMigration | Existing intentional technical media access remains protected by the Admin role. No broad Admin bypass was added to ordinary walk APIs. This tool was not executed. |

Application authorization prevents disclosure of private photo references through these pages/APIs. Previously known storage URLs or copies are not retroactively revoked by this patch; storage remains as previously implemented. Owner-initiated image downloads/sharing outside DoggyDrop remain deliberate existing actions. Neither a storage URL nor friendship makes the underlying Walk public.

### Copy and complete hardening regression review

Privacy Zone wording now states that Details, GPS and photos are owner-only independently of that setting. Its purpose is additional exclusion from walking-location aggregates, including historical walks; it does not disable other aggregate statistics or separate dog/park layers. Only the affected factual Terms statement changed. The Privacy placeholder and unresolved legal clauses were not rewritten.

The complete Epic review retains neutral public display-name fallbacks, transactional account deletion/rollback, deletion of the private account graph and both friendship directions, approved-bin retention with UserId NULL, conservative managed/shared-media cleanup, and the bounded owner-only JSON export. Export still contains the owner's complete GPS and photo metadata; it excludes other-user private content and credentials. No export or deletion collection was removed. Provider failure/backup limitations described above still apply.

### Verification

- Build with Razor compilation: PASS, zero errors. Existing MailKit/MimeKit NU1902 advisories and Planner nullable-reference warning remain outside this privacy change.
- Full .NET suite: **892/892 PASS**, zero failed/skipped. Ten new HTTP cases added to the previous 882-case tree. They cover anonymous/unrelated/friend direct URLs, ID enumeration, owner history/Memory/routes/photos, social mutations, notification links, anonymous leaderboard payloads, Community/profile/friend HTML, coarse-cell contributor thresholds and Privacy Zone exclusions. Existing photo-delivery and completed-walk tests now assert owner-only access while preserving owner rendering/delivery coverage. Existing export HTTP assertions additionally verify the owner's photo and walk plus exclusion of another user's GPS.
- Node: **82/82 PASS**, including Home/Active inline JavaScript syntax. Standalone JavaScript syntax: **12/12 PASS**. No JavaScript implementation changed.
- Isolated PostgreSQL 17 lifecycle: **5/5 PASS** (full graph, rollback, complete export, walk/photo deletion, managed/shared media). Synthetic disposable databases only; no migrations applied. Temporary server stopped successfully.
- Git whitespace and new-file whitespace checks: PASS. No migration, model snapshot, database model, schema, appsettings or Privacy-page change. No secret/config/build artifact added. External harnesses/logs remain outside the repository.

### Exact follow-up scope

**17 files changed in this follow-up**, compared with the saved 27-file starting tree:

1. DoggyDrop.Tests/PrivacyHttpTests.cs
2. DoggyDrop.Tests/PrivacySharingHttpTests.cs (new)
3. DoggyDrop.Tests/WalkPhotoDeliverySurfaceTests.cs
4. DoggyDrop.Tests/WalksControllerFinishTests.cs
5. DoggyDrop/Controllers/Api/WalksApiController.cs
6. DoggyDrop/Controllers/FriendsController.cs
7. DoggyDrop/Controllers/HomeController.cs
8. DoggyDrop/Controllers/WalksController.cs
9. DoggyDrop/Services/LocalLeaderboardService.cs
10. DoggyDrop/Services/WalkDataDeletion.cs
11. DoggyDrop/ViewModels/FriendsViewModel.cs
12. DoggyDrop/Views/Friends/Index.cshtml
13. DoggyDrop/Views/Home/Community.cshtml
14. DoggyDrop/Views/Home/Settings.cshtml
15. DoggyDrop/Views/Home/Terms.cshtml
16. DoggyDrop/Views/Walks/Index.cshtml
17. docs/privacy-data-audit.md

The complete uncommitted Epic 22.2 scope is **34 repository files: 19 modified tracked files + 15 new/untracked files**. All original 27 files remain; seven repository paths were added to the change set. Nothing was staged, committed, pushed or deployed. HEAD remains `760de2fa58091c0bb7cfd9fd07d38f5306421d6f`. No production or live media-provider access occurred.

### Current verdict and outstanding owner decisions

**Historical verdict, superseded by the subsequent strict review:** this pass reported BLOCKER 0 / HIGH 0 / MEDIUM 0 and SAFE TO COMMIT after closing the default-sharing MEDIUM. The strict review then reproduced two HIGH notification disclosures that this pass missed: private walk-start broadcasts and unsafe historical notification outputs. See the verified notification follow-up below for their resolution and the current verdict. No earlier pass constitutes approval of public launch or the Privacy Policy.

Owner/legal input is still required for the official controller postal address; legal bases for each processing purpose; approved retention/deletion/backup rules; child/age policy; active production providers/backends, regions, roles, DPAs and international-transfer safeguards; promotional reuse/copyright Terms; and any future explicit walk-sharing design. Existing provider URL/copy handling and operational cleanup verification remain relevant. No legal basis, retention period, age limit, transfer arrangement or production fact was invented. **Privacy remains unfinished; public launch and municipal outreach remain blocked pending those decisions and approved Privacy content.**

## Epic 22.2 notification privacy follow-up — 2026-09-28

This section supersedes earlier notification descriptions and readiness claims, while preserving their audit history. The final strict review found **BLOCKER 0 / HIGH 2 / MEDIUM 0**. Its reproduced failures were real even though the earlier 892 tests passed.

### Root causes and fixes

Both MVC walk-start paths (`Start` and `StartPlanned`) still called `NotifyFriendsAboutWalkStartAsync` after creating the owner's walk. That helper copied the owner's name, dog's name and private plan title into friends' notifications. Both calls and the helper are now removed. `NotificationService` also refuses `FriendStartedWalk` through both creation entry points, preventing accidental reintroduction via the general service. Repository search finds no remaining walk-start broadcast writer. Owner walk creation, plan association/usage, route generation and rewards are preserved.

The earlier sanitizer covered four generated bodies on writes/export/deletion, but omitted `FriendStartedWalk`, historical HTML/API readers, titles and links. `Services/NotificationPrivacy.cs` now owns the single type-aware privacy policy. It replaces complete sensitive titles/bodies with neutral Slovenian text and supplies safe links; it never tries to match or replace fragments of names/emails. Existing localized presentation delegates to that policy before its harmless achievement/level formatting.

### Complete notification classification

The repository has a string `UserNotification.Type`, not a `NotificationType` enum. All current writers, historical achievement handling, stored notifications, smart cards and UI type labels were traced.

| Types / type families | Classification and treatment |
|---|---|
| `FriendStartedWalk` | PRIVATE-WALK-RELATED. No new writes. Historical title/body become neutral unavailable-activity text; link is NULL. No owner, email/local-part, dog, plan or private walk ID survives presentation. |
| `WalkComment`, `WalkReaction` | PRIVATE-WALK-RELATED and potentially identity-bearing. Historical and future generated title/body are fixed neutral text; links are NULL. Cross-user mutations remain denied by the owner authorization checks. |
| `PlaydateInvite`, `PlaydateInterest` | POTENTIALLY IDENTITY-BEARING (dog, sender and location text in historical writers). Fixed neutral title/body; fixed `/Playdates` destination without an embedded identifier or copied stored link. Explicit playdate functionality remains. |
| `FriendRequest`, `FriendAccepted` | SAFE STRUCTURED/GENERIC. Existing writers use generic text without actor identity and link to Friends. Retained unchanged. |
| `BinApproved`, `NewBinNearby` | SAFE STRUCTURED/GENERIC. Own approved public-bin name or generic nearby-bin message, with existing bin/map destinations. Retained unchanged. Direct SQL writers were checked as well as the notification service. |
| `Achievement` (legacy), `Achievement:{key}`, `LevelUp:{level}`, `Streak:{kind}:{days}`, `FounderBadge:{area}` | SAFE STRUCTURED/GENERIC. Recipient's own achievements, catalog labels/ranks and public area names; no other-user identity. Retained, including existing localized level/first-walk presentation. Legacy achievement lookup reads timestamps internally, not raw text into a public DTO. |
| `WalkReminder`, `PopularParkNearby` | SAFE STRUCTURED/GENERIC. Generic owner reminder or public park/location suggestion, not another user's individual walk. Stored reminders and dynamic cards retain their existing behavior. |
| `FirstDogProfile`, `NearbyMapTips`, `WeatherAlert` | SAFE STRUCTURED/GENERIC dynamic cards. No historical actor payload; generic onboarding/map/weather text. No change to the existing browser weather data flow. |
| `General` (model default) | Generic system notification, with no current automatic identity-bearing writer. Preserved rather than blanket-redacting useful system content. New types/writers require classification before embedding actor content. |

### Output and deletion lifecycle

- `/Notifications` uses safe title/body/link, including accessible labels and both read/unread actions.
- `GET /api/notifications` replaces the existing title/body/link fields; there is no extra raw-body field.
- `GET /api/notifications/center` applies the policy to `latest`, `latestUnread`, raw-named fields and display fields. The shared layout widget consumes this safe payload; its count-only badge exposes no text. Smart-card messages are generated separately from generic/public/recipient-owned data.
- Both MVC and API mark-read paths use the safe link. Obsolete private-walk notifications cannot redirect or return a private walk ID. Recipient authorization and MVC antiforgery remain intact.
- Personal-data export keeps notification ID, type, creation/read timestamps and read state but replaces sensitive title/body/link. The rest of the owner's export is unchanged: account, dogs, full GPS, photo metadata, settings, contributions and other existing sections remain, without authentication secrets or traversed other-user profiles.
- The legacy schema has only a recipient FK and cannot reliably attribute a free-text historical notification to an actor. On successful account deletion, **all obsolete `FriendStartedWalk` rows are purged across recipients**. All remaining four sensitive types are normalized across recipients (title/body/link), and unused source keys are cleared. This deliberate type-wide cleanup avoids unreliable email/name matching. Safe generic notifications remain; the deleting recipient's own notifications cascade normally. All database cleanup is in the existing account-deletion transaction and is rolled back on failure.
- Historical sensitive raw storage can still exist before a successful account-deletion cleanup; all application output paths apply the policy immediately. No production cleanup command, data migration or manual database operation was performed.

### Permanent regressions and complete Epic self-review

`NotificationPrivacyHttpTests.cs` adds **34 cases**: both real authenticated/antiforgery walk-start POSTs with accepted friends; five sensitive types in both read/unread states through actual HTML, API, Center/latestUnread, export and mark-read responses; both notification-service broadcast entry points; four future sensitive write types; fifteen safe notification types/families; and account deletion with legacy sensitive storage and surviving safe notifications. Synthetic payloads contain an email/local-part, dog, private plan and private walk ID in titles/bodies/links. Tests assert those values are absent, neutral fields are present, useful metadata remains and another recipient cannot mark the record read.

The prior private-link test now requires a NULL API link as well as owner-only destination authorization. Existing SQLite/PostgreSQL full-graph and rollback tests now cover broadcast removal, comment title/body/link/source-key normalization, safe surviving-user export, and restoration of historical rows when deletion fails.

The complete Epic tree was rechecked: no public Email/UserName fallback is reintroduced; Google's missing-name fallback stays neutral; private walk/GPS/photo routes remain owner-only; friend relationships and coarse Community/leaderboard behavior remain; approved bins survive with no user link; pending bins, friendships and the private account graph are removed; managed/shared-media protections remain; export retains complete owned data and excludes secrets. The existing factual Terms/Privacy Zone wording remains consistent with owner-only walk activity and no default friend broadcast. The Privacy placeholder and broader legal copy are unchanged by this follow-up. GPS thresholds, distance/coalescing, Finish, photo delivery and schema are unchanged by this fix.

### Verification and exact scope

- `dotnet build` with `RazorCompileOnBuild=true`: PASS, zero errors. Existing MailKit/MimeKit NU1902 advisories remain; the existing Planner nullable warning also appeared on the initial recompile.
- Full `dotnet test`: **926/926 PASS**, zero failed/skipped (892 previous + 34 new).
- All Node tests: **82/82 PASS**, including Home/Active inline JavaScript syntax.
- Standalone JavaScript syntax: **12/12 PASS**. No JS implementation changes.
- Isolated PostgreSQL 17 lifecycle: **5/5 PASS** — full deletion graph, rollback, complete export, walk/photo deletion, managed/shared media. Synthetic disposable loopback databases only; no migrations applied. Temporary PostgreSQL server stopped successfully.
- Git diff and new-file whitespace checks: PASS. No migration/model snapshot/schema, appsettings, secrets, build artifacts or external harness added to repository scope.

The complete uncommitted Epic 22.2 tree is **40 repository files: 23 modified tracked + 17 new/untracked**. All original 34 paths are retained. This notification fix changes exactly **13 files** relative to the saved strict-review starting tree:

1. `DoggyDrop.Tests/NotificationPrivacyHttpTests.cs` (new)
2. `DoggyDrop.Tests/PrivacyLifecycleTests.cs`
3. `DoggyDrop.Tests/PrivacySharingHttpTests.cs`
4. `DoggyDrop/Controllers/Api/NotificationsApiController.cs`
5. `DoggyDrop/Controllers/NotificationsController.cs`
6. `DoggyDrop/Controllers/WalksController.cs`
7. `DoggyDrop/Services/AccountDataDeletion.cs`
8. `DoggyDrop/Services/NotificationPrivacy.cs` (new)
9. `DoggyDrop/Services/NotificationService.cs`
10. `DoggyDrop/Services/PersonalDataExport.cs`
11. `DoggyDrop/ViewModels/NotificationPresentation.cs`
12. `DoggyDrop/Views/Notifications/Index.cshtml`
13. `docs/privacy-data-audit.md`

Nothing was staged, committed, pushed or deployed. HEAD remains `760de2fa58091c0bb7cfd9fd07d38f5306421d6f`. No production or live media-provider access occurred.

### Current technical verdict and legal boundary

**Both reproduced HIGH findings are closed. BLOCKER 0 / HIGH 0 / MEDIUM 0 — SAFE TO COMMIT** for the reviewed Epic 22.2 technical scope. This verdict follows the tests and review above, and supersedes the premature earlier zero-findings statement. No commit is authorized by this task and none was made.

Owner/legal decisions remain unresolved: official controller postal address; legal bases per processing purpose; retention, deletion and backup rules; child/age policy; active production providers/backends, regions, roles, DPAs and transfer safeguards; promotional reuse/copyright Terms; and any future deliberate sharing feature. Managed-media cleanup still has the previously documented best-effort/provider-copy limitations. **Privacy is not finalized. Public launch and municipal outreach remain blocked until those decisions and approved Privacy content are completed.**
