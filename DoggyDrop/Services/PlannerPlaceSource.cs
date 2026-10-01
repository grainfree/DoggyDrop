using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed record PlannerDestination(string Name, string Type, double Latitude, double Longitude, double Priority);

// Current destinations only; no relationship to historical ParkLocation keys.
public static class PlannerPlaceSource
{
    public static async Task<IReadOnlyList<PlannerDestination>> LoadAsync(ApplicationDbContext db,
        double latitude, double longitude, double radiusKm)
    {
        var delta = radiusKm / 110;
        var longitudeDelta = Math.Min(180, delta / Math.Max(.000001, Math.Cos(Math.Min(90, Math.Abs(latitude) + delta) * Math.PI / 180)));
        var rows = await db.Places.AsNoTracking().ForPublicDetails()
            .Where(p => p.Category == PlaceCategory.DogPark || p.Category == PlaceCategory.DogFriendlyCafe || p.Category == PlaceCategory.PetShop)
            .Where(p => p.Latitude >= latitude - delta && p.Latitude <= latitude + delta &&
                p.Longitude >= longitude - longitudeDelta && p.Longitude <= longitude + longitudeDelta)
            .Select(p => new { p.Id, p.Name, p.Category, p.Latitude, p.Longitude }).ToListAsync();
        return rows.Where(p => PublicPlaceEligibility.ValidName(p.Name))
            .Select(p => new { Place = p, Metres = DuplicateCandidates.Distance(latitude, longitude, p.Latitude, p.Longitude) })
            .Where(p => p.Metres <= radiusKm * 1000)
            .OrderBy(p => p.Metres).ThenBy(p => p.Place.Id).Take(100)
            .Select(p => new PlannerDestination(p.Place.Name, p.Place.Category switch
            {
                PlaceCategory.DogPark => "park", PlaceCategory.DogFriendlyCafe => "cafe", _ => "shop"
            }, p.Place.Latitude, p.Place.Longitude, p.Metres)).ToArray();
    }
}
