# Bin photo orientation and Admin rotation

## Diagnosis

Before this hotfix, public Add and Admin Add/Edit called `ICloudinaryService.UploadTrashBinImageAsync`. Cloudinary uploaded original bytes without orientation/metadata normalization; its local fallback and `MissingCloudinaryService` also copied originals. R2 used `ImageOptimizationService`, but the TrashBin preset was permissive and could return original bytes after decode/encode failure. The shared optimizer already contained EXIF transforms and a 1200px/quality-76 bin preset, but those guarantees were not enforced on every storage path.

Public surfaces use `TrashBin.FullImageUrl`, with Cloudinary `f_auto,q_auto` delivery. Delivery changes are not a substitute for consistently oriented stored pixels. Approval does not reprocess images. The existing manual media-transfer tool also calls the bin optimizer; it now rejects unsuccessful bin processing before uploading or changing a reference. The tool was not run. No production bytes were inspected: this establishes a code-level normalization gap, not the individual cause of every reported historical image.

## New uploads

All three storage providers now require successful bin processing: strict JPG/PNG/WebP extension, MIME and signature matching; a complete decode; maximum 12 MiB, 12,000px per axis and 50 million pixels. SVG, GIF, HEIC/HEIF and other unsupported input is rejected rather than passed through. Supported phone files should be exported as JPEG when necessary.

Skia reads the encoded orientation, normalizes all eight EXIF orientations (including mirrors), resizes proportionally to at most 1200px and encodes WebP at quality 76. Re-encoding from pixels removes EXIF/GPS metadata. Bin processing never falls back to original bytes. Tests also exposed an existing EXIF-7 shared transform defect: its reflection axis drew pixels outside the output canvas. That primitive is corrected and pixel-tested for Profile, Walk and PlaceLogo too; their format, size, quality and fallback policies are otherwise unchanged. Failed uploads return validation feedback instead of silently submitting/replacing without the requested photo.

## Admin correction

The existing Admin Map/Edit surface has separate POST controls: Zavrti levo (270° clockwise), Zavrti desno (90°), Obrni za 180°. The endpoint is Admin-only and antiforgery protected. Only bin ID and a fixed operation are accepted. Server-side database state supplies the image target. Rotation normalizes orientation, physically rotates pixels, uploads a new image, then updates only ImageUrl. Public display/layout and approval semantics are unchanged. Existing photos are never processed in bulk.

Managed targets are exact versioned HTTPS Cloudinary URLs in the configured account's `doggydrop-trashbins` folder, configured R2 `trashbins/yyyy/MM/GUID` URLs (including existing `optimized`/`migrated` paths with bin ID and GUID), or local `/uploads/trashbins/GUID` files. Only JPG/PNG/WebP legacy targets are supported. Foreign accounts/folders, arbitrary URLs, transformed/ambiguous URLs, query strings, traversal, symlinked local paths and unsupported formats are rejected. HTTP reads disallow redirects, use a 30-second timeout and enforce the byte limit even without Content-Length. Missing/unsupported images have explanatory text; broken displayed images disable the controls. A failed download also returns a safe Slovenian message. Unsupported legacy images must be deliberately replaced through the existing upload form.

## Database/storage and concurrency

Each replacement has a new random key/URL, avoiding stale cached orientation. Existing originals are not overwritten. An atomic UPDATE compares the bin's current ImageUrl with the URL read before processing; two overlapping rotations cannot overwrite each other's result. The loser gets a conflict message and its unused copy is cleaned up. Sequential rotations use the newly stored state.

Only after successful database update is old-asset cleanup attempted, using a fresh reference check that also conservatively retains delivery aliases. Cleanup failure leaves an extra asset, never a broken reference. A database exception can have an ambiguous outcome; both old and new assets are retained even if an immediate read might appear to show no reference, because the server could still complete the write. Upload timeouts can likewise leave an unknown unused copy. These cases log category/bin ID without image bytes or credentials and require later deliberate reconciliation; there is no background deletion job. Normal Add/Edit replacement still preserves the old asset on failure.

## Verification and limits

Controlled corner-color JPEG fixtures exercise all eight real EXIF orientation values, metadata removal, resizing, left/right/half-turn and repeated rotations. Tests cover Cloudinary's actual SDK request stream and local fallback, R2, local copy/delete, allowlists, HTTP authorization/antiforgery/overposting, broken downloads, upload failures, database failure before and after execution, cleanup/reference failure, shared references and overlapping rotation requests. Test storage/database resources are local or mocked; no production service is called.

No schema or migration change. Municipal import, coordinates, GPS, rewards, Nearby, Places and auth configuration are unchanged. Remaining manual checks: phone-camera JPEGs, Admin controls on real mobile devices, and a dedicated non-production Cloudinary/R2 account for upload/download/delete and CDN verification. Local fallback remains subject to the deployment's existing local-disk persistence policy.
