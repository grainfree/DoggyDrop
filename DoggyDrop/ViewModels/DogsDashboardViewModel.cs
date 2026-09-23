using DoggyDrop.Models;
using DoggyDrop.Services;

namespace DoggyDrop.ViewModels
{
    public class DogsDashboardViewModel
    {
        public IReadOnlyList<Dog> Dogs { get; set; } = [];
        public Dog? SelectedDog { get; set; }
        public IReadOnlyList<Walk> RecentWalks { get; set; } = [];
        public IReadOnlyList<WalkPhoto> RecentPhotos { get; set; } = [];
        public int CompletedWalkCount { get; set; }
        public double TotalDistanceKm { get; set; }
        public int ParkLocationCount { get; set; }
        public int? ActiveWalkId { get; set; }
        public string? ActiveWalkDogName { get; set; }
        public DogProgressionProfile? Progression { get; set; }
        public DogProgressionLevelInfo? Level { get; set; }
    }
}
