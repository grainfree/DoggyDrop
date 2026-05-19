namespace DoggyDrop.Services
{
    public interface ILocalLeaderboardService
    {
        IReadOnlyList<(string Key, string Name)> Cities { get; }

        Task<LocalLeaderboardBoard> BuildAsync(string? cityKey = null);
    }
}
