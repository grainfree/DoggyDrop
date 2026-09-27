using System.Globalization;
using DoggyDrop.Models;

namespace DoggyDrop.Services;

public enum ImportRowStatus { Ready, PossibleDuplicate, Invalid }
public sealed record ImportSource(int Id, string Name, DataSourceType Type, DateOnly? DataDate);
public sealed record ImportMapping(int Latitude, int Longitude, int? Name, int? Address)
{
    public void Validate(int columns)
    {
        var mapped = new int?[] { Latitude, Longitude, Name, Address }.Where(x => x.HasValue).Select(x => x!.Value).ToArray();
        if (mapped.Any(x => x < 0 || x >= columns) || mapped.Distinct().Count() != mapped.Length)
            throw new BinImportException("Izberi različna veljavna stolpca za širino in dolžino; vsak stolpec uporabi le enkrat.");
    }
    public static ImportMapping Suggest(string[] headers)
    {
        int Find(params string[] names) => Array.FindIndex(headers, h => names.Contains(h.Trim().ToLowerInvariant()));
        int? Optional(params string[] names) { var index = Find(names); return index < 0 ? null : index; }
        return new(Find("lat", "latitude", "gps_lat"), Find("lon", "lng", "longitude", "gps_lon"),
            Optional("name", "ime", "description", "opis"), Optional("address", "naslov"));
    }
}
public sealed record ImportCandidate(int? BinId, int? RowNumber, string Name, string? Source, bool Approved, double Metres);
public sealed record ImportRow(int Number, string Name, double? Latitude, double? Longitude, string? Error,
    ImportRowStatus Status, IReadOnlyList<ImportCandidate> Candidates);
public sealed record ImportResult(int Imported, int Duplicates, int Invalid, int Unselected)
{
    public int Total => Imported + Duplicates + Invalid + Unselected;
}
public sealed record ImportPage(string Id, int Version, string FileName, ImportSource Source, DateTimeOffset ExpiresAt,
    string[]? Headers, ImportMapping? Mapping, IReadOnlyList<ImportRow> Rows, IReadOnlySet<int> Selected,
    int Page, int Total, int Ready, int Duplicates, int Invalid, ImportResult? Result = null);

public static class BinImportMapping
{
    public const int MaxName = 200;
    public const int MaxAddress = 180;
    public const int MaxDisplayName = MaxName + MaxAddress + 3;
    public static IReadOnlyList<ImportRow> Map(ImportCsv csv, ImportMapping mapping)
    {
        mapping.Validate(csv.Headers.Length);
        return csv.Rows.Select((cells, index) =>
        {
            var number = index + 2;
            if (cells.Length != csv.Headers.Length) return Invalid(number, "Število polj se ne ujema z glavo. Preveri ločilo in narekovaje.");
            var name = mapping.Name.HasValue ? cells[mapping.Name.Value].Trim() : "";
            var address = mapping.Address.HasValue ? cells[mapping.Address.Value].Trim() : "";
            if (name.Length > MaxName || address.Length > MaxAddress) return Invalid(number, "Ime/opis presega 200 znakov ali naslov 180 znakov.");
            // TrashBin has only Name. Make the exact persisted display text explicit in preview.
            name = name.Length == 0 ? "Koš" : name;
            if (address.Length != 0) name += " · " + address;
            var latitude = Coordinate(cells[mapping.Latitude]); var longitude = Coordinate(cells[mapping.Longitude]);
            if (latitude == null || longitude == null) return new ImportRow(number, name, latitude, longitude,
                "Manjka veljavna številčna širina ali dolžina. Tisoči niso podprti; uporabi decimalno piko ali vejico v enem CSV polju.", ImportRowStatus.Invalid, []);
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return new ImportRow(number, name, latitude, longitude, "Koordinate niso videti kot WGS84 latitude/longitude.", ImportRowStatus.Invalid, []);
            return new ImportRow(number, name, latitude, longitude, null, ImportRowStatus.Ready, []);
        }).ToArray();
    }
    private static ImportRow Invalid(int number, string message) => new(number, "", null, null, message, ImportRowStatus.Invalid, []);
    public static double? Coordinate(string text)
    {
        text = text.Trim();
        // No thousands or exponent syntax. Decimal commas are safe only inside a single parsed CSV field.
        if (!System.Text.RegularExpressions.Regex.IsMatch(text, @"^[+-]?[0-9]+(?:[.,][0-9]+)?$")) return null;
        return double.TryParse(text.Replace(',', '.'), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) ? value : null;
    }
}
