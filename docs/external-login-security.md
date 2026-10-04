# External-login account-linking security

Baseline: `f58c5971377fc52626caf218b9e82f8d7aba6887`.

## Trust boundary and fix

The former public callback automatically attached an unlinked provider to an existing account found by the provider's email. The confirmation POST also selected that account using browser-submitted email. Both then called `SignInAsync`, bypassing proof of existing-account ownership; the callback also fell through after failed linked-login policy checks. An isolated synthetic HTTP reproduction established a different synthetic Admin session. No production exploit was attempted or observed.

Existing-account authentication now resolves only Identity's `(LoginProvider, ProviderKey)` tuple and uses `ExternalLoginSignInAsync` with two-factor bypass disabled. Lockout, two-factor and sign-in restrictions cannot fall through into registration. Email changes or posted account identifiers cannot reassign an existing provider identity.

For an unlinked identity, a matching email/username produces a Slovenian conflict page, clears the external cookie and creates no account session or victim-account mutation. The same rule applies to Admin, password accounts and accounts already linked to another provider. The response does not expose roles, login methods, lockout, or other account metadata. Generic conflict disclosure is intentional, comparable to registration; no additional account-detail endpoint is introduced.

New external accounts remain supported without a local password. When provided, the authenticated provider email takes precedence over any posted email. Without an email claim, the normal registration form may collect an address for a **new** account only; a collision never links. Email syntax is validated. `EmailConfirmed` stays false: the current Google claim mapping does not establish a reviewed verified-email contract. Current `RequireConfirmedAccount` and `RequireConfirmedEmail` remain false, so legitimate new/linked external users can sign in. A future requirement for confirmed email would require an explicit verification delivery flow; do not switch that policy implicitly.

User creation and `AddLoginAsync` share a database transaction. Failed login attachment rolls back the new user. The existing unique normalized-username index (new username equals email) and composite provider-login primary key decide registration races; a loser is never attached to the winner. The email index itself is not unique; this hotfix makes no schema change. Identity failures/database exceptions are rendered as fixed, nontechnical copy, not raw error descriptions.

## Explicit linking and removal

Profile → Varnost → Povezane prijave opens the existing `/Identity/Account/Manage/ExternalLogins` route. Its local scaffold preserves the framework-supported flow: authenticated principal, antiforgery-protected challenge POST, configured provider allowlist, `ConfigureExternalAuthenticationProperties(..., user.Id)`, and `GetExternalLoginInfoAsync(user.Id)` at callback. This retains Identity's protected `XsrfId` binding and the external middleware's OAuth state/correlation protections. No client `UserId` selects a link target. A management-bound external cookie is rejected by public login/registration.

Linking deliberately relies on the current authenticated session, as the standard Identity management flow does; it does not introduce a separate password/reauthentication system. Existing external-only owners can authenticate with their existing provider and link another, without adding a password. A provider already attached elsewhere cannot be moved.

The framework's original removal page only hid the last-method removal button. The local route now checks that condition on the server too. A user can remove only their own login and must retain a password or another linked identity. Removal runs in a transaction: Identity's user concurrency stamp rejects a concurrent losing mutation and rolls back its login-row deletion. Normal password and bootstrap flows are unchanged.

Reference audited: ASP.NET Core Identity V5 `ExternalLogins.cshtml.cs`, v8.0.0, https://github.com/dotnet/aspnetcore/blob/v8.0.0/src/Identity/UI/src/Areas/Identity/Pages/V5/Account/Manage/ExternalLogins.cshtml.cs . Tests exercise the actual project Identity runtime with real local cookies, Razor and antiforgery.

## Provider, redirect and privacy audit

Only Google is configured. Configuration names: `Authentication:Google:ClientId`, `Authentication:Google:ClientSecret`, `GOOGLE_CLIENT_ID`, `GOOGLE_CLIENT_SECRET`. Values are neither read for this audit nor reproduced. The fixed middleware callback remains `/signin-google`; standard provider claim mapping and correlation remain intact. No application feature consumes external access/refresh tokens, so `SaveTokens` is now false. No token persistence call is added.

Return destinations are normalized to local URLs, otherwise `/`. Remote errors become fixed Slovenian text; raw errors, claims, authorization codes and tokens are not logged or rendered. Account pages use no-store responses. Razor encodes provider/user-facing text. No real identity provider is contacted by the tests.

Register/Login, ChangePassword/SetPassword and AdminBootstrap were inspected. Their password, authorization, bootstrap and credential handling remain unchanged. The standard Manage linking/removal route is the only linking mechanism. No Epic 23 onboarding/PWA/header/offline work is included.

## Verification and limits

Permanent `ExternalLoginSecurityTests` cover synthetic normal/Admin collisions, verified-claim collisions, tampered email/UserId, linked-tuple precedence, lockout/2FA/unconfirmed restrictions, new accounts, XSRF-bound explicit linking, cross-user/duplicate-provider rejection, last-login removal, concurrent registration/removal, transactional insert failure, antiforgery, missing cookies, unknown providers, safe return URLs and remote errors. Final self-review also added omitted, empty and malformed registration-email cases: these produce validation feedback rather than a null-reference/server error.

The isolated fixture uses SQLite with actual Identity stores, local HTTP and ephemeral cookie protection. It does not run application startup, bootstrap, migrations, email, storage or live OAuth. Browser fixtures consume local Razor captures and intercept every request. These demonstrate application-boundary handling, not Google's live consent/state exchange or a production PostgreSQL load test.

Remaining manual check after independent review/authorized rollout: legitimate Google sign-in for a linked account and a new account, collision guidance, authenticated linking and removal, plus iPhone/PWA account-page usability. This task does not perform those live checks, change production, or commit/push/deploy.

Final local verification: build PASS; full .NET **1,590/1,590**; external-login security **43/43**; AdminBootstrap **24/24**; password settings **33/33**; PrivacyHttp **63/63**; Node **245/245**; browser/layout/accessibility **60/60** (375/390/430/1024/1440, two heights, safe-area 0/34); established application JS syntax **33/33**, new browser fixture syntax PASS; explicit Razor rebuild and RazorCompile PASS; whitespace PASS. Existing MailKit/MimeKit NU1902 warnings remain unchanged.

Final hotfix self-review: BLOCKER 0, HIGH 0, MEDIUM 0, LOW 0. Synthetic tests and the current-tree credential scan found no new credible production secret. No model/schema/snapshot/migration changes. Exact scope: four modified files (public ExternalLogin Razor/model, Program token-storage setting, Profile link) and five new files (Manage ExternalLogins Razor/model, HTTP tests, browser fixture, this document). Index remains empty; no commit/push/deployment/production or real-account/provider test.
