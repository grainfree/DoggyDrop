using DoggyDrop.Models;

namespace DoggyDrop.Services
{
    public class MapStampService : IMapStampService
    {
        public MapStampCollection BuildCollection(IReadOnlyList<DogParkVisit> parkVisits)
        {
            var stamps = parkVisits
                .GroupBy(visit => visit.PlaceKey)
                .Select(group =>
                {
                    var first = group.OrderBy(visit => visit.VisitedAt).First();
                    var visitCount = group.Count();
                    return new MapStampItem
                    {
                        PlaceKey = first.PlaceKey,
                        Name = first.ParkName,
                        Area = first.Area,
                        VisitCount = visitCount,
                        FirstCollectedAt = group.Min(visit => visit.VisitedAt),
                        LastCollectedAt = group.Max(visit => visit.VisitedAt),
                        Rarity = CalculateRarity(first, visitCount)
                    };
                })
                .OrderByDescending(stamp => GetRarityRank(stamp.Rarity))
                .ThenByDescending(stamp => stamp.LastCollectedAt)
                .ToList();

            return new MapStampCollection
            {
                TotalStamps = stamps.Count,
                CommonCount = stamps.Count(stamp => stamp.Rarity == "Common"),
                RareCount = stamps.Count(stamp => stamp.Rarity == "Rare"),
                EpicCount = stamps.Count(stamp => stamp.Rarity == "Epic"),
                LegendaryCount = stamps.Count(stamp => stamp.Rarity == "Legendary"),
                Stamps = stamps
            };
        }

        private static string CalculateRarity(DogParkVisit visit, int visitCount)
        {
            if (visitCount >= 10) return "Legendary";
            if (visitCount >= 5) return "Epic";
            if (visitCount >= 2) return "Rare";
            if (IsRareArea(visit.Area) || IsRareArea(visit.Address)) return "Rare";
            return "Common";
        }

        private static bool IsRareArea(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized.Contains("obala") ||
                   normalized.Contains("prekmurje") ||
                   normalized.Contains("jezero") ||
                   normalized.Contains("ranca");
        }

        private static int GetRarityRank(string rarity)
        {
            return rarity switch
            {
                "Legendary" => 4,
                "Epic" => 3,
                "Rare" => 2,
                _ => 1
            };
        }
    }
}
