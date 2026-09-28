using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public sealed record PlaceImportMapping(int Name, int Latitude, int Longitude, int? Category = null,
    int? Address = null, int? Phone = null, int? Website = null, int? Description = null)
{
    public void Validate(int columns, PlaceCategory? fixedCategory)
    {
        if (fixedCategory.HasValue && !PlaceCategories.IsSupported(fixedCategory.Value))
            throw new BinImportException("Izberi podprto kategorijo.");
        if (!fixedCategory.HasValue && !Category.HasValue)
            throw new BinImportException("Izberi stolpec kategorije ali fiksno kategorijo ob nalaganju.");
        // A fixed category is authoritative; even a supplied Category mapping is ignored.
        var used = new int?[] { Name, Latitude, Longitude, fixedCategory.HasValue ? null : Category, Address, Phone, Website, Description }
            .Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        if (used.Any(x => x < 0 || x >= columns) || used.Distinct().Count() != used.Length)
            throw new BinImportException("Vsako polje preslikaj v drug veljaven stolpec.");
    }
    public static PlaceImportMapping Suggest(string[] headers)
    {
        int Find(params string[] names) => Array.FindIndex(headers, h => names.Contains(h.Trim().ToLowerInvariant()));
        int? Optional(params string[] names) { var i = Find(names); return i < 0 ? null : i; }
        return new(Find("name", "ime"), Find("latitude", "lat", "gps_lat"), Find("longitude", "lon", "lng", "gps_lon"),
            Optional("category", "kategorija"), Optional("address", "naslov"), Optional("phone", "telefon"),
            Optional("website", "websiteurl"), Optional("description", "opis"));
    }
}

public sealed record PlaceImportCandidate(int? PlaceId, int? RowNumber, string Name, double Metres);
public sealed record PlaceImportRow(int Number, string Name, PlaceCategory Category, double? Latitude, double? Longitude,
    string? Address, string? Phone, string? Website, string? Description, string? Error, ImportRowStatus Status,
    IReadOnlyList<PlaceImportCandidate> Candidates)
{
    // Only the explicitly importable fields can reach normal Place validation/persistence.
    public PlaceInput Input() => new() { Name = Name, Category = Category, Latitude = Latitude, Longitude = Longitude,
        Address = Address, Phone = Phone, WebsiteUrl = Website, Description = Description };
}

public static class PlaceImportMappingRules
{
    public static IReadOnlyList<PlaceImportRow> Map(ImportCsv csv, PlaceImportMapping mapping, PlaceCategory? fixedCategory)
    {
        mapping.Validate(csv.Headers.Length, fixedCategory);
        return csv.Rows.Select((cells, index) =>
        {
            if (cells.Length != csv.Headers.Length)
                return new PlaceImportRow(index + 2, "", 0, null, null, null, null, null, null,
                    "Število polj se ne ujema z glavo.", ImportRowStatus.Invalid, []);
            string? Optional(int? i) => i.HasValue && !string.IsNullOrWhiteSpace(cells[i.Value]) ? cells[i.Value].Trim() : null;
            var category = fixedCategory ?? ParseCategory(cells[mapping.Category!.Value]);
            var row = new PlaceImportRow(index + 2, cells[mapping.Name].Trim(), category,
                BinImportMapping.Coordinate(cells[mapping.Latitude]), BinImportMapping.Coordinate(cells[mapping.Longitude]),
                Optional(mapping.Address), Optional(mapping.Phone), Optional(mapping.Website), Optional(mapping.Description),
                null, ImportRowStatus.Ready, []);
            var errors = Errors(row).ToArray();
            return errors.Length == 0 ? row : row with { Status = ImportRowStatus.Invalid, Error = string.Join(" ", errors) };
        }).ToArray();
    }
    public static IEnumerable<string> Errors(PlaceImportRow row)
    {
        foreach (var error in row.Input().Validate()) yield return error.Message;
        if (!PublicPlaceEligibility.ValidName(row.Name)) yield return "Ime ne sme vsebovati kontrolnih znakov.";
    }
    public static PlaceCategory ParseCategory(string value) => PlaceCategories.All
        .FirstOrDefault(c => string.Equals(c.Category.ToString(), value.Trim(), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Key, value.Trim(), StringComparison.OrdinalIgnoreCase))?.Category ?? 0;
}

public sealed record PlaceImportPage(string Id, int Version, string FileName, ImportSource Source, PlaceCategory? FixedCategory,
    DateTimeOffset ExpiresAt, string[]? Headers, PlaceImportMapping? Mapping, IReadOnlyList<PlaceImportRow> Rows,
    IReadOnlySet<int> Selected, int Page, int Total, int Ready, int Duplicates, int Invalid, ImportResult? Result);
