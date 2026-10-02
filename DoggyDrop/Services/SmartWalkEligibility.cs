using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;
public static class SmartWalkEligibility
{
    // Place Admin edits take PostgreSQL row locks rather than the bin/water
    // advisory locks. Hold matching rows through save/start's transaction so
    // an edit cannot land between eligibility validation and the operation.
    public static async Task LockPlacesAsync(ApplicationDbContext db, IEnumerable<(double Latitude, double Longitude)> coordinates, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql()) return;
        if (db.Database.CurrentTransaction == null) throw new InvalidOperationException("Place eligibility locks require a transaction.");
        foreach (var point in coordinates.Distinct().OrderBy(p => p.Latitude).ThenBy(p => p.Longitude))
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT \"Id\" FROM \"Places\" WHERE \"Latitude\"={point.Latitude} AND \"Longitude\"={point.Longitude} ORDER BY \"Id\" FOR SHARE", ct);
    }
    // Smart V1 reuses the existing human-readable Reason field for a stable public
    // record reference. Type stays unchanged for Active/proximity/rewards. This
    // validator is used only for Smart plans; legacy plans retain their contract.
    private const string ReferencePrefix = "Izbran postanek na predlagani poti. Referenca #";
    public static async Task<bool> StopsAvailableAsync(ApplicationDbContext db, IReadOnlyList<PlannedWalkStop> stops, CancellationToken ct)
    {
        if (stops.Count > 12) return false;
        var references = new List<SmartWalkPoi>();
        foreach (var stop in stops)
        {
            if (stop.Type is "start" or "finish") continue;
            if (!stop.Reason.StartsWith(ReferencePrefix, StringComparison.Ordinal)
                || !int.TryParse(stop.Reason.AsSpan(ReferencePrefix.Length), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id <= 0) return false;
            references.Add(new(id, stop.Type, stop.Name, stop.Latitude, stop.Longitude));
        }
        return await PreviewAvailableAsync(db, references, ct);
    }
    public static async Task<bool> PreviewAvailableAsync(ApplicationDbContext db, IReadOnlyList<SmartWalkPoi> stops, CancellationToken ct)
    {
        foreach (var s in stops)
        {
            if (s.Kind is not ("bin" or "water" or "park" or "place")) return false;
            var valid = s.Kind switch
            {
                "bin" => await db.TrashBins.PublicBins().AnyAsync(p => p.Id == s.Id && p.Latitude == s.Latitude && p.Longitude == s.Longitude, ct),
                "water" => await db.WaterPoints.PublicWater().AnyAsync(p => p.Id == s.Id && p.Latitude == s.Latitude && p.Longitude == s.Longitude && p.DogAccess != WaterDogAccess.NotAllowed, ct),
                _ => PublicPlaceEligibility.ValidName(await db.Places.ForPublicDetails()
                    .Where(p => p.Id == s.Id && p.Latitude == s.Latitude && p.Longitude == s.Longitude && (s.Kind != "park" || p.Category == PlaceCategory.DogPark))
                    .Select(p => p.Name).SingleOrDefaultAsync(ct))
            };
            if (!valid) return false;
        }
        return true;
    }
    public static PlannedWalk ToPlan(string owner, int? dogId, SmartWalkSelection selected)
    {
        var stops = new List<PlannedWalkStop> { new() { Order = 1, Name = "Začetek sprehoda", Type = "start", Label = "Začetek", Latitude = selected.Input.Start.Latitude, Longitude = selected.Input.Start.Longitude } };
        foreach (var p in selected.Stops) stops.Add(new() { Order = stops.Count + 1, Name = SavedName(p.Name), Type = p.Kind, Label = p.Kind switch { "bin" => "Koš", "water" => "Pitnik", "park" => "Pasji park", _ => "Cilj" }, Reason = ReferencePrefix + p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Latitude = p.Latitude, Longitude = p.Longitude });
        if (selected.Input.WalkType == "loop") stops.Add(new() { Order = stops.Count + 1, Name = "Vrnitev na začetek", Type = "finish", Label = "Cilj", Latitude = selected.Input.Start.Latitude, Longitude = selected.Input.Start.Longitude });
        return new()
        {
            OwnerId = owner,
            DogId = dogId,
            Title = "Predlagan sprehod",
            AreaKey = SmartWalkPolicy.Version,
            AreaName = "Izbrano izhodišče",
            TargetDistanceKm = selected.Input.Minutes * SmartWalkPolicy.MetresPerMinute / 1000,
            EstimatedDistanceKm = selected.Route.DistanceMeters / 1000,
            EstimatedMinutes = (int)Math.Ceiling(selected.Route.DurationSeconds!.Value / 60),
            IncludeBins = selected.Input.Bins,
            IncludeWater = selected.Input.Water,
            IncludePark = selected.Input.Park,
            Stops = stops,
            RoutePoints = selected.Route.Points.Select((p, i) => new PlannedWalkRoutePoint { Order = i + 1, Latitude = p.Latitude, Longitude = p.Longitude }).ToList()
        };
    }
    private static string SavedName(string name) => name.Length <= 120 ? name : name[..(char.IsHighSurrogate(name[119]) ? 119 : 120)];
}
