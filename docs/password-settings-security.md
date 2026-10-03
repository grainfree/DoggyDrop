# Self-service password settings

Baseline: `4b4ea4380c41bf337e8a06dcac8d876266db0f6a` (`Secure administrator bootstrap`).

## Audit and routes

DoggyDrop uses ASP.NET Core Identity with `ApplicationUser`, the EF Identity store, `UserManager`, `SignInManager` and the default Identity UI package (8.0.11). Login, registration, external login and parts of Manage were already scaffolded. ChangePassword and SetPassword were available from the Identity UI package and referenced by the existing Manage navigation, but not exposed in the application's custom Profile. Neither had dedicated application regression tests. Google login can create accounts without a local password.

The feature adapts the existing Identity routes, without adding an account-management system:

- `/Home/UserProfile` → **Varnost → Spremeni geslo**.
- GET/POST `/Identity/Account/Manage/ChangePassword`: authenticated local-password users, including Admin, change only their own password.
- GET/POST `/Identity/Account/Manage/SetPassword`: the existing Identity first-password flow for authenticated accounts without a local password. ChangePassword directs those accounts here. Existing local-password users are redirected back to ChangePassword on GET **and POST**. External login associations remain intact.

Both pages return naturally to Profile. They explicitly use the application's layout, standard password inputs and its bottom-navigation clearance convention. No custom password-field or session-management JavaScript is involved. There are no new external providers.

## Password and session contract

ChangePassword resolves the account exclusively through `UserManager.GetUserAsync(User)` and calls `ChangePasswordAsync(user, currentPassword, newPassword)`. The current password is required; submitted account IDs/emails are not bound. SetPassword uses `HasPasswordAsync` and `AddPasswordAsync` and cannot replace an existing password. No application code hashes passwords, writes password hashes, or persists plaintext. Confirmation is required to match before mutation.

Existing Identity validators remain authoritative. No global password policy is changed. Current defaults are minimum length 6, one distinct character, uppercase, lowercase, digit and non-alphanumeric requirements. The bootstrap-only minimum is unrelated and unchanged. The UI translates known Identity error codes into Slovenian; it does not render arbitrary validator descriptions. Unknown errors get a fixed message. Same-password submission follows Identity: it is permitted if policy passes and produces a new security stamp; this is tested, not recommended for rotation.

`ChangePasswordAsync` validates the current password, invokes the configured new-password validators and updates the security stamp with the password. `AddPasswordAsync` also updates it. After success, `RefreshSignInAsync` refreshes the current Identity cookie and preserves the session's authentication properties. A fixed success message is stored in TempData and displayed after a redirect. No passwords or hashes are put in TempData, redirects or responses.

No security-stamp validation interval was configured previously, so the default **30 minutes** remains. Other cookies may remain usable until their next validation after that interval; they are not immediately logged out. Tests advance a test-only clock (without sleeps) and demonstrate old-cookie rejection and refreshed-cookie survival. Global cookie/session settings are unchanged.

Framework references inspected:

- [Identity UserManager 8.0.11](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Identity/Extensions.Core/src/UserManager.cs): ChangePasswordAsync, AddPasswordAsync, UpdatePasswordHash.
- [Identity ChangePassword UI](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Identity/UI/src/Areas/Identity/Pages/V5/Account/Manage/ChangePassword.cshtml.cs) and [SetPassword UI](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Identity/UI/src/Areas/Identity/Pages/V5/Account/Manage/SetPassword.cshtml.cs).
- [SignInManager](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Identity/Core/src/SignInManager.cs) and [SecurityStampValidatorOptions](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Identity/Core/src/SecurityStampValidatorOptions.cs).

## Authorization, request protection and privacy

Both pages explicitly require authentication and antiforgery. Razor's normal form token is retained. Missing/invalid tokens fail without mutation. Admin has no bypass or ability to select another account.

The existing ASP.NET request-limiter infrastructure now applies a dedicated shared `password-settings` policy to both routes: **5 POST requests per authenticated account per 5-minute fixed window**, no queue. Reads do not consume the limit. Tabs share the same partition, and changing from ChangePassword to SetPassword does not evade it. Rejection returns HTTP 429, a value-free Slovenian explanation and `Retry-After: 300`. Counts are per application process and reset with process restart; the current single-instance deployment is compatible. Reassess distributed enforcement if scaling changes. The limiter bounds attempts, not just incorrect-password checks; fixed-window boundaries can allow adjacent bursts.

`ChangePasswordAsync` does not increment login lockout counters. Existing Login calls `PasswordSignInAsync(..., lockoutOnFailure: false)`; that pre-existing login behavior and all global lockout options are unchanged. This task limits password-settings attempts, not the separate login endpoint.

No new request-body or password logging is added. Inputs and attempted ModelState values are cleared on validation failures, keeping error messages. Password values are never repopulated into HTML. Known errors do not expose Identity internals. Both pages request no-store caching; transport follows existing application configuration. No GET password mutation, target-account parameter, open return redirect, new credential configuration or schema exists.

## Verification and owner action

Permanent HTTP tests use isolated SQLite, real Identity cookies, Razor, antiforgery, rate limiting and generated synthetic credentials. They do not run Program/bootstrap/migrations or call production, email or external-login services. Cases include old/new authentication, wrong current password, policy, required/matching fields, same password, Admin, IDOR, CSRF, no-local-password, limiter, stamp/session lifecycle, response/log non-disclosure, Profile discoverability and login/logout/registration/authorization regressions.

The permanent browser fixture consumes sanitized local Razor captures from `DOGGYDROP_PASSWORD_CAPTURE` and writes evidence to external `DOGGYDROP_PASSWORD_RESULTS`. All network requests are intercepted. Widths: 320, 375, 390, 430, 768, 1024, 1440; normal and reduced-height keyboard-oriented viewports; 0/34px modeled safe area; empty/error/long-policy/success/first-password states. It checks field labels, autocomplete, focus, error/status text, native keyboard submission, overflow and scroll clearance above visible bottom navigation. It does not prove physical iPhone keyboard or password-manager behavior.

AdminBootstrap remains initial setup only. Do **not** set its configuration to an existing Admin's desired password to attempt rotation. The historical exposed bootstrap credential is not repeated, used or tested here. History is not rewritten. After a separately approved deployment, the owner must use this self-service flow with their current password and a new unique password to rotate any potentially affected account. If the current password is unknown, use a trusted account-recovery process; bootstrap is not recovery. Other sessions invalidate on the stamp schedule above, not immediately.

Remaining physical checks: iPhone Safari and installed PWA, native password manager/autofill/paste, actual keyboard scrolling, safe-area/browser toolbar behavior, and owner-controlled password change after deployment. No actual credential rotation or production session revocation is performed in this task. No Epic 23, schema, migration or model changes.

Local verification: build PASS (unchanged MailKit/MimeKit warnings); .NET 1,547/1,547; new password settings HTTP/security 33/33; bootstrap 24/24; existing account/privacy HTTP 63/63; Node 245/245; password settings browser/layout/accessibility/keyboard 168/168; established application JS syntax 33/33 plus the new browser script; explicit Razor rebuild and RazorCompile PASS; whitespace checks PASS. Current repository scan found no additional credible production-capable secrets. Only intended implementation, tests and this documentation are in the working tree; captures/results remain outside it. No commit, push or deployment was performed.
