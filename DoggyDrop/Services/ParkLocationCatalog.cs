namespace DoggyDrop.Services;

public sealed record ParkLocation(
    string PlaceKey,
    string Name,
    string Area,
    string Address,
    double Latitude,
    double Longitude,
    string Type = "park",
    string Label = "Pasji park");

public static class ParkLocationCatalog
{
    public static IReadOnlyList<ParkLocation> All { get; } =
    [
        Park("46.0689", "14.4697", "Pasji park Koseze", "Ljubljana", "Ob Koseskem bajerju"),
        Park("46.0476", "14.5515", "Pasji park Stepansko naselje", "Ljubljana", "Ob Pesarski cesti"),
        Park("46.0615", "14.5210", "Pasji park Severni park", "Ljubljana", "Vilharjeva / Zelezna cesta"),
        Park("46.0567", "14.4965", "Pasji park Tivoli", "Ljubljana", "Tivoli park"),
        Park("46.0519", "14.5650", "Pasji park Fuzine", "Ljubljana", "Fuzinski park"),
        Park("46.0742", "14.5148", "Pasji park Bezigrad", "Ljubljana", "Ob Dunajski cesti"),
        Park("46.0415", "14.4769", "Pasji park Vic", "Ljubljana", "Ob PST poti"),
        Park("46.0727", "14.4723", "Pasji park Mostec", "Ljubljana", "Rekreacijsko obmocje Mostec"),
        Park("45.5426", "13.7184", "Pasji park Koper", "Obala", "Semedela"),
        Park("45.5365", "13.6619", "Pasji park Izola", "Obala", "Ob obalni promenadi"),
        Park("45.5056", "13.6024", "Pasji park Lucija", "Obala", "Lucija center"),
        Park("46.6606", "16.1664", "Pasji park Murska Sobota", "Prekmurje", "Mestni park / sportni del"),
        Park("46.5487", "15.6453", "Pasji park Tabor", "Maribor", "Ob sportnem parku Tabor"),
        Park("46.5625", "15.6480", "Pasji park Mestni park", "Maribor", "Ob robu Mestnega parka"),
        Park("46.5338", "15.5959", "Pasji park Pekre", "Maribor", "Pekre"),
        Park("46.2387", "15.2675", "Pasji park Celje", "Celje", "Ob Savinji"),
        Park("46.2289", "15.2518", "Pasji park Lava", "Celje", "Lava"),
        Park("46.2449", "14.3617", "Pasji park Kranj", "Kranj", "Zlato polje"),
        Park("46.2508", "14.3325", "Pasji park Strazisce", "Kranj", "Strazisce"),
        Park("45.7998", "15.1771", "Pasji park Novo mesto", "Novo mesto", "Portoval"),
        Park("45.9561", "13.6482", "Pasji park Nova Gorica", "Nova Gorica", "Ob sportnem centru"),
        Park("46.4216", "15.8788", "Pasji park Ptuj", "Ptuj", "Ranca Ptuj"),
        Park("46.5095", "15.0808", "Pasji park Slovenj Gradec", "Slovenj Gradec", "Sportni park"),
        Park("46.3622", "15.1147", "Pasji park Velenje", "Velenje", "Ob Velenjskem jezeru")
    ];

    public static ParkLocation? Find(string? placeKey)
    {
        if (string.IsNullOrWhiteSpace(placeKey)) return null;
        return All.FirstOrDefault(park => string.Equals(park.PlaceKey, placeKey.Trim(), StringComparison.Ordinal));
    }

    private static ParkLocation Park(string latitude, string longitude, string name, string area, string address)
    {
        var latitudeValue = double.Parse(latitude, System.Globalization.CultureInfo.InvariantCulture);
        var longitudeValue = double.Parse(longitude, System.Globalization.CultureInfo.InvariantCulture);
        var placeKey = $"park-{latitudeValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}-{longitudeValue.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        return new ParkLocation(placeKey, name, area, address, latitudeValue, longitudeValue);
    }
}
