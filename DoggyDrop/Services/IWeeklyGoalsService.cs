using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public interface IWeeklyGoalsService
{
    Task<WeeklyGoalsViewModel> GetForUserAsync(string userId);
}
