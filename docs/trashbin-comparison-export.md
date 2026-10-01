# Admin TrashBin comparison export

`GET /AdminBins/ExportComparison` downloads a UTF-8 CSV with BOM from the existing **Koši — viri podatkov** (`/AdminBins`) page. It uses the existing Admin role authorization; anonymous and ordinary authenticated users cannot download it. The response has `Cache-Control: private, no-store, no-cache` and the existing Admin no-cache behavior. It is a read-only GET, with no antiforgery requirement or mutation.

The exact columns are `BinId,Latitude,Longitude,IsApproved,DateAdded,ApprovedAt,DataSourceId`. `BinId` maps to `TrashBin.Id`; the other names map directly to existing properties. Both approved and pending rows are included, ordered by Id, independently of page filters/pagination. There is no additional soft-delete/lifecycle field in TrashBin; only existing rows are returned.

Coordinates use invariant round-trip numeric formatting, booleans use `true`/`false`, and dates use ISO-8601 UTC round-trip format with a `Z` suffix. Database timestamps are UTC; an unspecified DateTime Kind from a provider is interpreted as UTC. Null ApprovedAt and DataSourceId are empty cells. Fields are CSV-escaped. All values originate from typed numbers, booleans and dates, never free-text cells that could inject spreadsheet formulas.

The attachment filename is `doggydrop-trashbins-yyyyMMddTHHmmssZ.csv`, using actual UTC when the export starts. It labels a single ordered database query, not a long-lived snapshot or a promise that bins cannot change afterward. There are no metadata rows. DateAdded and ApprovedAt retain their stored meanings; neither is relabeled as physical verification.

An explicit AsNoTracking projection selects only the seven columns. No contributor identity, name, email, photo URL, reports, usage history, walk/GPS data, private DataSource metadata or credentials are exported. The action does not call SaveChanges or update approval/timestamps. The file contains infrastructure data including pending locations and is still restricted to Admins.

The intended use is owner-authorized offline reconciliation with the separately prepared OSM dog-waste research. This feature does not perform that comparison, approve/import OSM records, or modify the importer. Obtain a fresh export before a later reviewed import; the existing inclusive 20-metre duplicate rule considers both approved and pending bins. No schema, Privacy Policy, routing or map behavior changes.
