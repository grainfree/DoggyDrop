using DoggyDrop.Models;

namespace DoggyDrop.ViewModels
{
    public class NotificationsViewModel
    {
        public IReadOnlyList<UserNotification> Notifications { get; set; } = [];

        public int UnreadCount { get; set; }

        public IReadOnlyList<SmartNotificationCard> SmartCards { get; set; } = [];
    }

    public class SmartNotificationCard
    {
        public string Type { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string Body { get; set; } = string.Empty;

        public string? LinkUrl { get; set; }
    }
}
