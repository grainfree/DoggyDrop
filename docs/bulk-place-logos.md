# Bulk Place logo assignment (Epic 19.2)

## Workflow

Admin Places now has a bounded name filter alongside the existing state, category
and source filters. Filter by a chain name (for example Mr.Pet), mark the intended
rows and choose **Nastavi logotip**. Only explicitly selected IDs on the displayed
page are submitted; there is no hidden selection of every matching database row.
The existing activate/deactivate/source actions and Bin bulk controls are unchanged.

The dedicated confirmation page shows the count and a compact, expandable list.
Choose one PNG/JPG/WebP and confirm. Browser preview uses a local object URL only;
it performs no upload. Confirmation stores one managed asset and assigns its exact
same stored URL to every selected Place. Feedback reports the affected count.
Cancelling before confirmation uploads nothing. Optional bulk removal was omitted;
the existing single-Place remove/replace workflow remains available.

A 33-store workflow therefore needs 33 explicitly selected IDs, one upload and one
logical database save. No brand-specific application code or real store data is
included. Name/category/current logo/source values in form submissions are not
authoritative. Source and all unrelated Place fields remain unchanged.

## Selection and category policy

The existing `AdminBulkTools.MaxSelection` limit is 100 submitted IDs. IDs must be
positive and existing; duplicates are removed server-side. Oversized submissions
are rejected before deduplication. Both preview and apply re-query the database.

The entire batch is rejected if any selected category is not commercial/service
according to `PlaceCategories.Get(category).IsCommercial`. All five current
commercial categories work; DogPark, DogBeach and unknown categories are rejected
with a Slovenian explanation. Their public category-symbol behavior is unchanged.

## Existing managed-image pipeline

The bulk service calls `PlaceLogoUploadPolicy.IsSupportedAsync` and the existing
`IPlaceLogoStorage.UploadAsync` exactly once. `CloudinaryPlaceLogoStorage` still uses
`ImageOptimizationService` with the PlaceLogo preset: PNG/JPG/WebP, matching MIME
and signature, <=5 MiB, dimensions <=4096 on either side, <=16,000,000 pixels,
actual decoding, normalization/metadata removal, transparent-padding handling and
WebP optimization. No bulk-specific weakening or alternate image pipeline exists.

The repository's current managed **Place-logo** pipeline is Cloudinary-only (or
MissingPlaceLogoStorage when unavailable). R2/local support in other media flows
does not imply Place-logo support. This Epic adds neither an R2/local adapter nor
filesystem deletion. A returned URL must pass the existing managed-ID and expected
cloud delivery checks; CSV LogoUrl input remains unsupported and untouched.

## Security and concurrency

Both actions are Admin-only POSTs with antiforgery and bounded request/form sizes.
GET cannot assign a logo. A purpose-specific Data Protection ticket contains only
the server-read IDs/UpdatedAt ticks, owner ID and a ten-minute expiry. Cross-owner,
expired, modified and invalid tickets are rejected. Browser URLs, old-logo values,
storage keys, category and source fields cannot override server state.

Apply re-queries the selection and verifies the captured versions before uploading.
This deliberately protects the interval from confirmation-page creation, not just
the shorter upload/commit interval. Any changed/deleted/unsupported selected Place
requires a fresh selection. A replay after successful assignment fails its version
check before another upload.

Tracked original UpdatedAt values are retained during upload. After upload, one
explicit database transaction assigns only LogoUrl and
`PlaceUpdates.NextUpdatedAt(previous)` to every selected Place, followed by one
SaveChanges and commit. Existing optimistic concurrency predicates detect edits
or overlapping bulk assignments during upload/save. A conflict rolls back the
whole selection. UpdatedAt remains monotonic at microsecond precision, so a
previously opened single-Place edit form becomes stale.

## Failure ordering and cleanup

1. Selection/image rejection or upload failure: no Place changes or old deletion.
2. Database failure: transaction disposal/rollback happens before cleanup. A fresh
   independent database context checks whether the new upload is referenced. It is
   deleted only when confirmed unreferenced. If commit succeeded despite an error,
   the live asset stays. If reference verification fails, retain a possible orphan.
   The Admin is told to refresh/check the list because the outcome may be uncertain.
3. Successful commit: collect distinct managed old asset identities excluding the
   new asset's identity, even if delivery URLs differ. One fresh batched reference
   query checks Places outside the selected IDs. Only unreferenced identities
   proceed to managed storage deletion, once per asset.
4. Reference/deletion failure: retain assets, log a safe count-only warning and
   keep the successful assignment. No rollback for obsolete-asset cleanup failure.

`IPlaceLogoReferenceReader` retains its single-URL method, now with canonical
reference semantics. The batched method returns managed identities. The production
reader projects only distinct, nonempty LogoUrl strings from a fresh context, in
one query capped at 4,097 results. More than 4,096 distinct references causes cleanup
to retain every candidate; it never treats a truncated result as proof of absence.
No full Place rows are materialized. At most 100 candidate identities and 4,096
reference strings are resolved per cleanup. This is one bounded projection, not
an indexed asset-ID lookup; the database can scan the LogoUrl column for DISTINCT.
There is no query/upload per selected Place. Deletes remain per unreferenced asset.

Deletion, reference matching, bulk deduplication and new-asset exclusion use the
same parser in `PlaceLogoDelivery`. Its immutable cleanup identity contains the
Cloudinary cloud and case-sensitive public ID; resource/delivery type is fixed
to image/upload. It is not persisted or exposed in public Place models.

The strict managed path is `doggydrop/places/logos/{32-hex GUID}.webp` after a numeric
version component. Versions, hostname casing, query strings and fragments do not
change cleanup identity. Only the existing `f_auto,q_auto` and
`c_fit,w_128,h_128/f_auto,q_auto` delivery prefixes are recognized as transformations.
Arbitrary path segments are never stripped. The original path is checked before
URI normalization: encoded paths, dot segments, duplicate separators, backslashes,
unexpected ports, credentials, resource types and namespace collisions fail closed.
Cloud and public-ID case remain significant; foreign clouds are distinct identities
and cannot become own-cloud deletion targets. The storage adapter revalidates the
same identity before sending its public ID to Cloudinary deletion.

An unresolved Cloudinary reference aborts cleanup conservatively, retaining all
candidates. Unknown transformations or malformed legacy Cloudinary rows can thus
leave orphans until reviewed separately. External non-Cloudinary URLs do not become
managed assets. Query failure and the reference cap likewise retain assets. Single
Place replacement/removal uses this same fresh canonical reference reader, including
failed-save cleanup, rather than exact LogoUrl equality. Public URLs are not rewritten;
upload-response validation stays strict and public fragment acceptance is unchanged.

Fresh checks plus unique upload IDs and concurrency protect a winner's logo:
neither bulk nor single-place forms accept existing asset IDs as assignment input.
No distributed transaction is claimed. Unexpected provider/DB failure can leave
an orphan; retaining it is preferable to deleting a live shared asset.

## Integration and validation

No Map, Discovery, Details, Saved Places, SEO, Featured, Amenities, DataSource,
importer, GPS, privacy, account, notification, authentication or Bin-image behavior
is changed. Existing public projections derive the same category-safe transformed
logo from the shared raw managed URL. No SavedPlace relation or SEO field changes.

Permanent service/HTTP tests cover one upload for 33/100 Places, mixed/shared old
assets, outside references, failed upload/save/cleanup, unknown reference state,
an exception after actual commit, stale tickets, concurrent edits and existing
single-edit removal of one shared reference. HTTP tests use the real image
optimizer/storage adapter with a fake Cloudinary transport, and render the actual
Home, Discovery, Details/friendly route, OG and Saved Places output. Four Node
tests cover local preview lifecycle and rejection of unsupported preview input.

Isolated PostgreSQL checks exercise shared assignment, cleanup, rollback, ambiguous
commit, concurrent edit, overlapping batches, mixed delivery aliases, an aliased
winner and an aliased reference after an ambiguous commit. The four original strict
review reproductions also use the real optimizer/storage adapter with a fake
Cloudinary transport. The retained methods accept explicit synthetic local options;
no production configuration or migrations are consulted. External harnesses and
layout fixtures are not repository files.

The identity fix adds permanent alias, namespace/cloud/encoding, bounded-reference,
same-new-identity, ambiguous-commit and real single-place replacement/removal tests.
The original 32 bulk cases and 15 PlaceLogo cases remain, along with Place/Bin import
regressions. The 33-Place HTTP cases remove the final aliased reference and assert
exactly one old-asset deletion. Existing MailKit/MimeKit advisory and Planner
nullability warnings remain outside this change.

Verified after the identity fix on 2026-09-28: build/Razor passed; 1,057/1,057 .NET
tests (54 added for this fix), including 51 bulk service/HTTP cases, 35 identity
cases, 15 existing PlaceLogo cases, 45 Place-import and 50 Bin-import cases;
86/86 Node tests; 13/13 isolated PostgreSQL checks (the four original alias
reproductions now retain the live asset); 24/24 layout cases at the six requested
widths; 13/13 standalone JavaScript syntax checks plus Home/Active inline syntax;
git whitespace and UTF-8 checks passed. Strict self-review found no remaining
BLOCKER/HIGH/MEDIUM issue. Complete Epic 19.2 scope is 16 repository files;
external review helpers remain outside the repository.

Before real use, confirm ownership/permission for the logo, select the correct
branches and verify the real storage integration in an authorized environment.
Real mobile browsers, screen readers, CDN propagation and live upload/delete
behavior remain manual checks. This task does not commit, push, deploy, apply
migrations or access production/live storage.
