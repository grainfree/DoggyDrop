using DoggyDrop.Models;

namespace DoggyDrop.Services
{
    public interface ISeasonalEventService
    {
        IReadOnlyList<SeasonalEventProgress> BuildProgress(
            IReadOnlyList<Walk> completedWalks,
            IReadOnlyList<DogParkVisit> parkVisits);

        string GetCurrentMapTheme(DateOnly? today = null);
    }
}
