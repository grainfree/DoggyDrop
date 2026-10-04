# Identity adversarial audit

Baseline: `1c87ddecff876691e267c8ebdf526135370798d1` (`Secure external account linking`), clean `main` before work. No production, real account, SMTP delivery or external identity-provider calls were used. Epic 23 implementation remains out of scope. Changes are deliberately uncommitted for review.

## Confirmed findings and repairs

| Severity | Finding demonstrated locally | Repair |
| --- | --- | --- |
| HIGH | Forgot-password email used an attacker-controlled request Host in its reset URL. The same absolute-link pattern existed in registration and framework resend/change-email pages. A victim following such a link could disclose the bearer token. | Identity email pages generate absolute URLs from the already-validated `Seo:PublicOrigin`, through a scoped URL helper. Relative navigation, incoming host/scheme and cookies are unchanged. Registration, forgot, resend and Manage/Email are covered, including framework pages. |
| HIGH | Authenticated `Home/UpdateProfile` accepted a POST without antiforgery and mutated the display name. SameSite Lax is defense in depth, not proof of request intent (including same-site attackers). | Add the existing MVC antiforgery attribute; the existing form already emits a token. Principal-based ownership and photo handling stay unchanged. |
| HIGH | Shared layout used `no-referrer-when-downgrade` on reset/confirmation pages. Intercepted browser requests included a synthetic token query in two third-party resource referrers. | Identity-only response `Referrer-Policy: no-referrer`, matching layout metadata, and `Cache-Control: no-store`. Non-Identity layout policy is unchanged. Three permanent browser probes prevent regression; every request is intercepted. |
| MEDIUM | Six wrong Admin-password attempts did not lock the account: Login explicitly disabled failure counting and had no other password-guessing limit. | Enable standard Identity persisted lockout counting, retaining its five failures / five minutes defaults and existing 2FA/sign-in handling. |
| MEDIUM | Twenty-two consecutive recovery submissions all dispatched mail in the mock sender. Resend and registration also provided unbounded mail-triggering paths. | One shared process-local fixed-window budget: 20 email-triggering POST requests per effective remote IP per 15 minutes, no queue, 429 and Retry-After 900. It covers registration, forgot, resend and Manage/Email. Reads and ordinary login are not charged. |
| MEDIUM | Framework ConfirmEmailChange persisted Email/EmailConfirmed before SetUserName failed for a conflicting username. The local fixture left mismatched/duplicate account email state. | Override the existing confirmation page with collision checking and one transaction around ChangeEmailAsync + SetUserNameAsync. Identity still validates user, target email, token purpose, expiry and stamp. Failure rolls back both writes; concurrency/uniqueness constraints remain authoritative. |

Evidence is outside the repository: `C:/Codex/identity-audit/baseline-probes.log`, `expanded.log`, `referrer-probe.log`, local TRX results and browser captures. None contains a production credential. The baseline four security assertions failed before the first fixes. The email-change assertion failed before its transaction fix. The intercepted browser probe reported a referrer leak before the Identity-only policy fix, without sending any request externally.

## Audited surface and authority

| Flow | Authority / result |
| --- | --- |
| Register | Small InputModel only; generated user ID; CreateAsync enforces password policy and unique normalized username (email). Role/UserId/EmailConfirmed/PasswordHash overposting cannot grant Admin. Case-insensitive duplicate Admin registration fails without mutation. Existing confirmation policy remains unchanged. AcceptTerms product/legal wording was not redesigned. |
| Login | Identity username/password validation, standard lockout and optional 2FA. Unknown and wrong-password cases retain the same generic feedback. Remember-me and framework cookie issuance remain unchanged. |
| Logout | POST + Razor antiforgery; GET does not sign out. LocalRedirect rejects external destinations. |
| ExternalLogin | Existing tuple `(LoginProvider, ProviderKey)` only; no email-based reassignment. Existing Admin/ordinary/verified-email collisions and posted-email tampering remain rejected without victim mutation. New external accounts need no local password and are not assumed email-confirmed. |
| Manage/ExternalLogins | Current principal plus protected XsrfId/provider state; explicit link cannot target another user or move an already-linked provider. Last-method checks, transaction and Identity concurrency stamp protect unlinking. |
| ChangePassword | Current principal/current password, antiforgery, five POSTs/user/five minutes shared with SetPassword, Identity password validation and refreshed current cookie. |
| SetPassword | Only accounts without a password; cannot replace an existing password or choose another account. |
| ForgotPassword | Existing account lookup sends through a mock sender in tests. Unknown accounts receive the same confirmation redirect. Canonical HTTPS reset links and shared email budget now apply. No real mail was sent. |
| ResetPassword | Token bound to user/purpose/lifetime/stamp. Cross-user/Admin substitution, tampering, expiry, replay and token use after password change fail. Successful reset changes stamp without creating a session; concurrent resets have one winner. |
| ConfirmEmail | Framework token/user binding; invalid/expired/cross-user tokens cannot confirm the target. Valid replay is idempotent and does not create a session. |
| ResendEmailConfirmation | Framework generic response retained; cannot change stored target address. Canonical links and shared email budget apply. |
| Manage/Email / ConfirmEmailChange | Current principal requests a token bound to the new address. Public callback requires that token; no arbitrary UserId/email authority. Confirmation is now atomic. Anonymous confirmation never creates a session; matching current sessions follow RefreshSignInAsync. |
| Profile / Manage/Index | Current principal selects the account. Profile accepts display name/file only; Manage/Index accepts phone only. Injected user/role/security fields are ignored. Hostile display names render encoded, not executable HTML. |
| PersonalData / Download / Delete | Existing authorization and Razor antiforgery; exported data belongs to current principal. Deletion requires the current password if present and explicit confirmation; external-only accounts use their authenticated session and confirmation. Existing transactional deletion/privacy/media-cleanup semantics are unchanged and regression-tested. |
| AdminBootstrap | Explicit configuration, initial creation only, no existing-account elevation/reset and transactional role membership. Full previous suite passes. |
| Admin authorization | All 13 Admin controllers have Admin authorization, no AllowAnonymous action bypass. MediaMigration and Home/TestEmail also have Admin guards. Representative ordinary-user HTTP reads are denied before action execution. |
| AccessDenied / errors | No custom authorization bypass. Production retains its generic error handler. No new token/password/claims logging. |

Custom Identity scaffolds and relevant Program setup were inspected. Framework-backed Email, ConfirmEmail, ResendEmailConfirmation and the former ConfirmEmailChange implementation were inspected against the ASP.NET Core v8 source and exercised through the actual installed Identity UI in the local HTTP fixture. No new provider, MFA, reset system, email system or profile system was introduced.

## Cookies, tokens, privacy and concurrency

Application cookies remain framework-managed: HttpOnly, SameSite Lax, 14-day ticket lifetime with sliding expiration; Program's cookie policy forces Secure outside Development. Login/AccessDenied paths are unchanged. Google correlation/state handling is untouched; Google remains the only configured external provider. SaveTokens remains false. Tests never contact Google or load production configuration.

The security-stamp interval remains approximately 30 minutes. Password changes/reset update stamps; the current password-settings session is refreshed, older cookies are rejected at the next validation interval. This is not immediate global logout. Existing tests demonstrate that behavior.

Normal password policy remains minimum six characters with upper/lower/digit/non-alphanumeric and one unique character. RequireConfirmedAccount and RequireConfirmedEmail remain false; the audit does not introduce a new confirmation requirement or invent confirmation for external users.

Reset/confirmation tokens exist in their necessary email links and form submissions, not application database token rows. Passwords are hashed by Identity and are not reflected in tested HTML/TempData. Provider access/refresh tokens, authorization codes and full claims are not explicitly logged/stored by the application. Repository logging suppresses routine Microsoft.AspNetCore request Information logs; operators must not enable token-bearing URL/body logging at proxies or application diagnostics. Historical/production logs were not accessed. Existing SMTP error logging may include provider error text; no secret-bearing SMTP exception was demonstrated, and SMTP architecture was not changed.

External registration/link/unlink, new email confirmation, and password reset races were exercised on isolated relational SQLite with real Identity stores. Existing PostgreSQL-backed application constraints were inspected; no production or new PostgreSQL deployment test was performed. Normalized username and provider tuple uniqueness plus Identity concurrency stamps remain in force. Email's index is not unique; new-account username equals email, and the repaired email-change transaction preserves that invariant. The audit does not inspect or repair any historical production inconsistency.

## Effective limits and remaining limitations

| Endpoint family | Effective application behavior |
| --- | --- |
| Password login | Persisted Identity account lockout after five failures, five minutes; no new per-IP login limiter. Unknown usernames have generic failure. |
| ChangePassword / SetPassword | Existing shared five POSTs per user per five minutes; process-local, zero queue. |
| Register / Forgot / Resend / Manage Email | New shared 20 POST requests per effective IP per 15 minutes, zero queue. Registration may send both a user email and an Admin notification, so this is a request budget, not a count of individual messages. |
| Reset / Confirm callbacks | Cryptographic token/user/purpose/lifetime/stamp validation; no arbitrary new attempt limiter. |
| External completion/linking | Existing protected provider cookie/correlation/XSRF, unique provider tuple, auth/antiforgery where applicable; no new arbitrary limiter. |

The email budget resets with the process and is per instance. Shared NAT/proxy peers share a budget. It uses Connection.RemoteIpAddress after existing trusted forwarded-header processing, never a raw caller-supplied X-Forwarded-For header. Correct proxy trust/scale remains an owner operational check; this audit does not alter Render or forwarded-header configuration.

Remaining LOW observations:

1. Existing registration/reset/lockout responses have minor account-existence differences. No roles, password presence or other high-value metadata is disclosed; generic wrong-password/unknown-login behavior is preserved. These differences alone did not yield a material account compromise.
2. Some pre-existing malformed local-return inputs produce a generic server error via LocalRedirect rather than a graceful fallback. They never redirect externally. The custom reset/framework confirmation pages also retain strict Base64 decoding; malformed-code UX remains a separate minor robustness issue. Production error handling remains generic.

The existing Admin-only Home/TestEmail GET can send a fixed diagnostic email; it does not mutate account/security state or expose arbitrary recipients. It was not called with real mail. No broader Admin redesign was performed.

## Verification

- Build PASS (existing MailKit/MimeKit NU1902 warnings unchanged; packages not upgraded).
- Full .NET: 1,642/1,642.
- New Identity adversarial suite: 52/52 (registration/login/logout/session 13; recovery/email/token 27; profile/authorization 12).
- Existing external-login/linking: 43/43; password settings: 33/33; bootstrap: 24/24; privacy HTTP/IDOR/deletion: 63/63.
- Node: 245/245.
- New confirmation-page layouts/accessibility: 40/40 across 375/390/430/1024/1440, two heights, safe-area 0/34; token-referrer browser checks: 3/3. All network requests intercepted.
- Established application JS syntax: 33/33; new browser fixture syntax PASS.
- Explicit Razor rebuild, RazorCompile and whitespace checks PASS.
- Current-tree credential scan: no new credible production-capable secret; candidate values suppressed. Prior reviewed synthetic literals/placeholders/config references/vendor false positives remain.

Second-pass review attacked host/forwarded-host link generation, the actual framework mail routes, shared limiter bypass between routes, token/account/address substitution, simultaneous resets/email changes, provider linking/removal, Admin overposting, XSS, principal-based profile updates and return-URL variants. No remaining BLOCKER/HIGH/MEDIUM was found after repair. All six material findings above are fixed in this uncommitted tree; LOW 2 remain as documented.

Exact scope: 10 files, four modified and six new. Modified: Program.cs, Login.cshtml.cs, HomeController.cs, Shared/_Layout.cshtml. New: IdentityRequestSafety.cs, ConfirmEmailChange.cshtml and .cshtml.cs, IdentityAdversarialTests.cs, Browser/identity-audit.cjs, this document. No model/schema/migration/snapshot changes. No external audit helpers, captures, screenshots, DBs, credentials or build artifacts are included.

Remaining owner checks after independent review/authorized rollout: legitimate Google linked/new login and explicit linking, real SMTP reset/confirmation delivery with canonical origin, trusted proxy/IP-budget behavior, and iPhone Safari/PWA token-page usability. No live provider/device claim is made.

Verdict for the repaired local tree: **SAFE TO RESUME EPIC 23**. Security changes still require the owner's review/commit decision; this task has made no commit, push, deployment, production access or production migration.
