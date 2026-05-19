namespace DoggyDrop.Services
{
    public interface INotificationService
    {
        Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null);
        Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24);
    }
}
