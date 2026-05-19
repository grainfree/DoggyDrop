using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services
{
    public class LocalLeaderboardService : ILocalLeaderboardService
    {
        private readonly ApplicationDbContext _context;

        private static readonly IReadOnlyList<CityAnchor> CityAnchors =
        [
            new("maribor", "Maribor", 46.5547, 15.6459),
            new("ljubljana", "Ljubljana", 46.0569, 14.5058),
            new("celje", "Celje", 46.2397, 15.2677),
            new("kranj", "Kranj", 46.2397, 14.3556),
            new("koper", "Koper", 45.5481, 13.7301)
        ];

        public LocalLeaderboardService(ApplicationDbContext context)
        {
            _context = context;
        }

        public IReadOnlyList<(string Key, string Name)> Cities => CityAnchors
            .Select(city => (city.Key, city.Name))
            .ToList();

        public async Task<LocalLeaderboardBoard> BuildAsync(string? cityKey = null)
        {
            var city = CityAnchors.FirstOrDefault(item => item.Key == NormalizeCity(cityKey)) ?? CityAnchors[0];
            var weekStart = DateTime.UtcNow.Date.AddDays(-6);

            var walks = await _context.Walks
                .Include(walk => walk.Dog)
                .Include(walk => walk.Owner)
                .Include(walk => walk.Points)
                .Where(walk => walk.Status == "Completed")
                .ToListAsync();
            var cityWalks = walks
                .Where(walk => IsNearCity(city, GetWalkLatitude(walk), GetWalkLongitude(walk)))
                .ToList();

            var bins = await _context.TrashBins
                .Include(bin => bin.User)
                .Where(bin => bin.IsApproved)
                .ToListAsync();
            var cityBins = bins
                .Where(bin => IsNearCity(city, bin.Latitude, bin.Longitude))
                .ToList();

            var parkVisits = await _context.DogParkVisits.ToListAsync();
            var cityParkVisits = parkVisits
                .Where(visit => IsNearCity(city, visit.Latitude, visit.Longitude))
                .ToList();

            var photos = await _context.WalkPhotos
                .Include(photo => photo.Walk)
                    .ThenInclude(walk => walk!.Dog)
                .Include(photo => photo.User)
                .Include(photo => photo.Reactions)
                .ToListAsync();
            var cityPhotos = photos
                .Where(photo => photo.Walk != null && IsNearCity(city, GetWalkLatitude(photo.Walk), GetWalkLongitude(photo.Walk)))
                .ToList();

            return new LocalLeaderboardBoard
            {
                CityKey = city.Key,
                CityName = city.Name,
                MostDistance = Rank(cityWalks
                    .GroupBy(walk => new { walk.OwnerId, OwnerName = GetDisplayName(walk.Owner) })
                    .Select(group => new LocalLeaderboardEntry
                    {
                        UserId = group.Key.OwnerId,
                        Label = group.Key.OwnerName,
                        SubLabel = $"{group.Count()} sprehodov",
                        Score = group.Sum(walk => walk.DistanceMeters) / 1000,
                        ScoreText = $"{group.Sum(walk => walk.DistanceMeters) / 1000:0.0} km"
                    })),
                MostDiscoveries = Rank(BuildDiscoveryEntries(cityBins, cityParkVisits)),
                MostHelpful = Rank(cityBins
                    .Where(bin => !string.IsNullOrWhiteSpace(bin.UserId))
                    .GroupBy(bin => new { bin.UserId, OwnerName = GetDisplayName(bin.User) })
                    .Select(group => new LocalLeaderboardEntry
                    {
                        UserId = group.Key.UserId,
                        Label = group.Key.OwnerName,
                        SubLabel = $"{group.Count()} dodanih kosev",
                        Score = group.Sum(bin => bin.UsefulVotes + bin.UsedCount),
                        ScoreText = $"{group.Sum(bin => bin.UsefulVotes + bin.UsedCount):0} helpful"
                    })),
                BestPhotos = Rank(cityPhotos
                    .GroupBy(photo => new { photo.UserId, OwnerName = GetDisplayName(photo.User) })
                    .Select(group => new LocalLeaderboardEntry
                    {
                        UserId = group.Key.UserId,
                        Label = group.Key.OwnerName,
                        SubLabel = $"{group.Count()} fotografij",
                        ImageUrl = group.OrderByDescending(photo => photo.Reactions?.Count ?? 0).FirstOrDefault()?.ImageUrl,
                        Score = group.Sum(photo => photo.Reactions?.Count ?? 0),
                        ScoreText = $"{group.Sum(photo => photo.Reactions?.Count ?? 0):0} tack"
                    })),
                TopDogsThisWeek = Rank(cityWalks
                    .Where(walk => walk.StartedAt >= weekStart)
                    .GroupBy(walk => new
                    {
                        walk.DogId,
                        DogName = walk.Dog?.Name ?? "Pes",
                        DogPhotoUrl = walk.Dog?.PhotoUrl,
                        OwnerName = GetDisplayName(walk.Owner)
                    })
                    .Select(group => new LocalLeaderboardEntry
                    {
                        DogId = group.Key.DogId,
                        Label = group.Key.DogName,
                        SubLabel = $"{group.Key.OwnerName} · {group.Count()} sprehodov",
                        ImageUrl = group.Key.DogPhotoUrl,
                        Score = group.Sum(walk => walk.DistanceMeters) / 1000,
                        ScoreText = $"{group.Sum(walk => walk.DistanceMeters) / 1000:0.0} km"
                    }))
            };
        }

        private static IEnumerable<LocalLeaderboardEntry> BuildDiscoveryEntries(
            IReadOnlyList<TrashBin> cityBins,
            IReadOnlyList<DogParkVisit> cityParkVisits)
        {
            var binDiscoveries = cityBins
                .Where(bin => !string.IsNullOrWhiteSpace(bin.UserId))
                .GroupBy(bin => new { bin.UserId, OwnerName = GetDisplayName(bin.User) })
                .Select(group => new
                {
                    UserId = group.Key.UserId!,
                    group.Key.OwnerName,
                    Count = group.Count()
                });

            var parkDiscoveries = cityParkVisits
                .GroupBy(visit => new { visit.UserId, visit.PlaceKey })
                .GroupBy(item => item.Key.UserId)
                .Select(group => new
                {
                    UserId = group.Key,
                    OwnerName = "DoggyDrop uporabnik",
                    Count = group.Count()
                });

            return binDiscoveries
                .Concat(parkDiscoveries)
                .GroupBy(item => item.UserId)
                .Select(group =>
                {
                    var first = group.First();
                    var count = group.Sum(item => item.Count);
                    return new LocalLeaderboardEntry
                    {
                        UserId = first.UserId,
                        Label = first.OwnerName,
                        SubLabel = "kosi + parki",
                        Score = count,
                        ScoreText = $"{count:0} odkritij"
                    };
                });
        }

        private static IReadOnlyList<LocalLeaderboardEntry> Rank(IEnumerable<LocalLeaderboardEntry> entries)
        {
            return entries
                .Where(entry => entry.Score > 0)
                .OrderByDescending(entry => entry.Score)
                .Take(5)
                .Select((entry, index) =>
                {
                    entry.Rank = index + 1;
                    return entry;
                })
                .ToList();
        }

        private static string NormalizeCity(string? cityKey)
        {
            return string.IsNullOrWhiteSpace(cityKey)
                ? "maribor"
                : cityKey.Trim().ToLowerInvariant();
        }

        private static bool IsNearCity(CityAnchor city, double? latitude, double? longitude)
        {
            if (!latitude.HasValue || !longitude.HasValue)
            {
                return false;
            }

            return GetDistanceKm(city.Latitude, city.Longitude, latitude.Value, longitude.Value) <= 35;
        }

        private static double? GetWalkLatitude(Walk walk)
        {
            return walk.Points?.OrderBy(point => point.RecordedAt).FirstOrDefault()?.Latitude;
        }

        private static double? GetWalkLongitude(Walk walk)
        {
            return walk.Points?.OrderBy(point => point.RecordedAt).FirstOrDefault()?.Longitude;
        }

        private static string GetDisplayName(ApplicationUser? user)
        {
            if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            {
                return user.DisplayName;
            }

            return user?.Email ?? user?.UserName ?? "DoggyDrop uporabnik";
        }

        private static double GetDistanceKm(double lat1, double lng1, double lat2, double lng2)
        {
            const double earthRadiusKm = 6371;
            var dLat = ToRadians(lat2 - lat1);
            var dLng = ToRadians(lng2 - lng1);
            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRadians(double degrees) => degrees * Math.PI / 180;

        private sealed record CityAnchor(string Key, string Name, double Latitude, double Longitude);
    }
}
