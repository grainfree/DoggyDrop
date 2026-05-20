using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services
{
    public interface IOsmWalkPlannerService
    {
        Task<PlannedWalkRoute?> PlanAsync(
            double startLatitude,
            double startLongitude,
            double targetDistanceKm,
            IReadOnlyList<TrashBin> bins,
            string walkStyle,
            string dogEnergy,
            bool includeBins,
            bool includePark,
            bool includeWater,
            bool includeDogFriendly,
            CancellationToken cancellationToken = default);
    }
}
