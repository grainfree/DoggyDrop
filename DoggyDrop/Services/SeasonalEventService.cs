using DoggyDrop.Models;

namespace DoggyDrop.Services
{
    public class SeasonalEventService : ISeasonalEventService
    {
        private const string ParksMetric = "parks";
        private const string WaterWalksMetric = "waterWalks";
        private const string WinterWalksMetric = "winterWalks";

        private static readonly IReadOnlyList<SeasonalEventDefinition> Events =
        [
            new()
            {
                Key = "spring-explorer",
                Name = "Spring Explorer",
                Description = "Discover 10 different parks with your dog.",
                RewardName = "Bloom Scout badge",
                Theme = "spring",
                StartsOn = new DateOnly(2026, 3, 1),
                EndsOn = new DateOnly(2026, 5, 31),
                Metric = ParksMetric,
                Target = 10
            },
            new()
            {
                Key = "summer-adventure",
                Name = "Summer Adventure",
                Description = "Walk near lakes, rivers or water stops.",
                RewardName = "Lake Paw profile frame",
                Theme = "summer",
                StartsOn = new DateOnly(2026, 6, 1),
                EndsOn = new DateOnly(2026, 8, 31),
                Metric = WaterWalksMetric,
                Target = 8
            },
            new()
            {
                Key = "winter-paw",
                Name = "Winter Paw Challenge",
                Description = "Complete winter walks and unlock a cold-weather badge.",
                RewardName = "Snow Paw badge",
                Theme = "winter",
                StartsOn = new DateOnly(2026, 12, 1),
                EndsOn = new DateOnly(2027, 2, 28),
                Metric = WinterWalksMetric,
                Target = 12
            }
        ];

        public IReadOnlyList<SeasonalEventProgress> BuildProgress(
            IReadOnlyList<Walk> completedWalks,
            IReadOnlyList<DogParkVisit> parkVisits)
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return Events
                .Where(item => IsActiveOrUpcoming(item, today))
                .Select(item => new SeasonalEventProgress
                {
                    Key = item.Key,
                    Name = item.Name,
                    Description = item.Description,
                    RewardName = item.RewardName,
                    Theme = item.Theme,
                    EndsOn = item.EndsOn,
                    Current = CalculateMetric(item, completedWalks, parkVisits),
                    Target = item.Target
                })
                .OrderBy(item => item.IsComplete)
                .ThenBy(item => item.EndsOn)
                .ToList();
        }

        public string GetCurrentMapTheme(DateOnly? today = null)
        {
            var current = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
            if (current.Month == 12) return "christmas";
            if (current.Month == 10 && current.Day >= 20) return "halloween";
            return Events.FirstOrDefault(item => item.StartsOn <= current && item.EndsOn >= current)?.Theme ?? "default";
        }

        private static bool IsActiveOrUpcoming(SeasonalEventDefinition item, DateOnly today)
        {
            return item.EndsOn >= today && item.StartsOn <= today.AddDays(45);
        }

        private static int CalculateMetric(
            SeasonalEventDefinition item,
            IReadOnlyList<Walk> completedWalks,
            IReadOnlyList<DogParkVisit> parkVisits)
        {
            return item.Metric switch
            {
                ParksMetric => parkVisits
                    .Where(visit => IsWithinEvent(visit.VisitedAt, item))
                    .Select(visit => visit.PlaceKey)
                    .Distinct()
                    .Count(),
                WaterWalksMetric => completedWalks.Count(walk =>
                    IsWithinEvent(walk.StartedAt, item) &&
                    (walk.PlannedWalk?.Stops?.Any(stop => stop.Type == "water") == true ||
                     walk.PlannedWalk?.IncludeWater == true)),
                WinterWalksMetric => completedWalks.Count(walk => IsWithinEvent(walk.StartedAt, item)),
                _ => 0
            };
        }

        private static bool IsWithinEvent(DateTime dateTime, SeasonalEventDefinition item)
        {
            var date = DateOnly.FromDateTime(dateTime);
            return date >= item.StartsOn && date <= item.EndsOn;
        }
    }
}
