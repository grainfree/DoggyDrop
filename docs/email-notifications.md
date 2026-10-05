# Application email — Epic 25

## Pre-implementation decisions (section 272)

Baseline: 55c6ad4831bf97f5e4cfb1c561377ea8821615a4. The full specification through
section 400 has been received. Audit: C:/Codex/epic250/prerequisite-audit.md
(external working evidence, not a runtime dependency).

Existing UserNotifications are an in-app inbox, not a delivery queue. Identity
mail remains outside preferences/outbox. Legacy test-email GET actions are removed.
The SMTP adapter must expose safe outcomes without leaking raw provider errors.

V1 sends only review results: bin approved/rejected, photo approved/rejected,
location approved/rejected, issue approved/rejected (reviewed, never “resolved”).
No receipt email: existing submission UI and history already acknowledge receipt.
No separate welcome: multiple Identity activation paths make a new hook unnecessary.
No imports, confirmations, walks, Smart Walk, digest or marketing email.

ContributionUpdates defaults ON, user-configurable; this is a product default,
not marketing consent. Digest/marketing remain OFF and unsent; no unused public
unsubscribe tokens or future-category toggles. Identity security mail is unaffected.

## Exact proposed schema (before migration)

Two additive tables only:

- NotificationPreferences: UserId varchar(450) primary/FK to AspNetUsers CASCADE;
  ContributionUpdates boolean default true. No marketing-consent representation.
- NotificationOutbox: Id bigint identity PK; RecipientUserId varchar(450) required
  FK to AspNetUsers CASCADE; Type integer (8 fixed review event types); PayloadVersion
  integer default 1; BinId integer nullable FK TrashBins SET NULL; ContributionId
  bigint nullable FK BinContributions SET NULL; EventKey varchar(100) unique;
  Status integer (Pending, Processing, Sent, Failed, Suppressed); CreatedAt UTC;
  NextAttemptAt UTC; AttemptCount integer default 0; AttemptLimit integer default 5;
  LeaseToken nullable UUID; LeaseUntil nullable UTC; SentAt nullable UTC;
  Failure enum integer default None. Checks bound state/attempt values.

The payload is typed columns, not arbitrary JSON/HTML: type/version plus object
references. No address snapshot, text, names, photos, GPS, notes or tokens stored.
Indexes: unique EventKey; (Status, NextAttemptAt); (Status, LeaseUntil); FK indexes.
Outbox defaults above are constructor/property defaults applied by application
inserts; only ContributionUpdates has a database default constraint.
Preferences and all recipient-specific outbox rows, including Sent, delete with
account. Existing domain tables are unchanged.

## Delivery contract

Intent is added within the existing business transaction/SaveChanges. Database
EventKey uniqueness complements existing serialized/concurrency-checked transitions.
No SMTP in business transactions. Current confirmed account email and latest
preference are checked when queuing and again immediately before delivery. Deleted,
unconfirmed or opted-out recipients are suppressed; enabling later does not replay
suppressed historic activity. Changed email uses the current confirmed address.

Worker processes at most 10 records per poll, one short claim at a time, every
30 seconds. PostgreSQL FOR UPDATE SKIP LOCKED claims, five-minute lease and UUID
completion fencing prevent ordinary concurrent delivery. A send has a 30-second
timeout. Expired claims recover; expired final attempts become Failed. Retry delays
after attempts 1–4: 5 minutes, 30 minutes, 2 hours, 12 hours; automatic maximum 5.
Admin retry of an early terminal failure resumes the remaining five-attempt
budget. After exhaustion, each Failed retry grants one extra attempt, preserving
count; lifetime cap 10. Sent/Suppressed/Pending/Processing cannot be manually requeued.

SMTP is outside DB transactions. SMTP acceptance followed by lost response or DB
failure can cause duplicate delivery on recovery; exactly-once is not claimed.
Deletion/opt-out observed by the final pre-send check suppresses delivery. Changes
after that check race the in-flight SMTP handoff and cannot retract an accepted
message. Deleted queued rows cannot be claimed or recovered later. Never claim
Sent means read or confirmed email ownership.

Hosted delivery requires Production and Notifications:DeliveryEnabled=true.
Missing, false or malformed enablement is disabled; no setting is activated by this Epic. Development/test SMTP
transport is always non-delivering; tests inject fakes. Missing SMTP configuration
produces safe terminal Configuration failure, not a retry storm or liveness failure.
Existing EmailSettings and Username/Password aliases remain; values are not exposed.

Templates use fixed Slovenian subjects, HTML encoding, inline email-safe styles,
plain text and SeoSite canonical links. No remote images, attachments or tracking.
Approved public bins can link to /?binId=ID; unavailable targets and rejected cases
use owner history. No magic login; ordinary target authorization remains.

Profile links to a compact own-account preference form (CSRF protected). Admin
delivery list is paginated, masked and no-store; retry is Admin POST with CSRF,
current-state conditional update and no recipient/type/body input.

Owner export includes effective preference plus type/status/created/sent history,
not payload, lease, attempt/failure internals. No automatic retention sweep; long-term
retention and provider copies are OWNER/LEGAL REVIEW. Public launch and municipal
outreach remain BLOCKED. No marketing legal basis/consent/compliance is claimed.

## Required owner checks after review

Verify SMTP provider/region/DPA and sender configuration; explicitly authorize
delivery enablement; test actual sender identity, Gmail/Apple Mail/Outlook, SPF,
DKIM, DMARC, bounce handling/provider quotas, Render multi-instance operations,
Admin usability and physical iPhone/PWA settings. No real-email or DNS/provider
changes occur in this task. Future digest/marketing needs separate consent,
unsubscribe, retention and commercial-email review.

## Verification and review (2026-10-05)

Migration: `20261004214441_AddActivityEmailOutbox`. Up/Down and snapshot contain
only the two new tables, checks, indexes and foreign keys. A populated disposable
PostgreSQL database preserved users, bins, water, Places, DataSources, dogs, walks,
saved plans, contributions and confirmations across migration and rollback.
The older confirmation migration fixture now finds its predecessor by migration
name rather than assuming confirmations will always be the latest migration.

Local synthetic verification: 1,774 .NET tests (81 focused email cases), 268 Node
tests, 21 email PostgreSQL checks plus 30 existing PostgreSQL regressions, 280 new
browser cases plus 2,231 existing browser/layout/performance cases. Build, explicit
Razor rebuild/RazorCompile, 31 application JS syntax checks, model consistency and
whitespace checks passed. No real email/provider/production calls were made.

Queue draining with fake SMTP, connection pooling disabled and concurrent browser
tests: 0/10/100/1,000 rows took approximately 47/4,210/41,700/377,687 ms. Every row
was sent once in the tested normal path; batches never exceeded ten. These are
local synthetic measurements, not production throughput or exactly-once guarantees.

No remaining Epic 25 BLOCKER/HIGH/MEDIUM findings. Non-blocking limitations are
the documented ambiguous SMTP acceptance/crash window and unperformed real-email
client/provider/physical-device validation. Existing MailKit/MimeKit NU1902
warnings are unchanged. Public launch/municipal outreach and legal approval remain
separate gates. Delivery is not enabled, and work remains uncommitted for review.
