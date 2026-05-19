using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services
{
    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;

        public NotificationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task CreateAsync(string userId, string type, string title, string body, string? linkUrl = null)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            _context.UserNotifications.Add(new UserNotification
            {
                UserId = userId,
                Type = type,
                Title = title,
                Body = body,
                LinkUrl = linkUrl,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            });

            await _context.SaveChangesAsync();
        }

        public async Task CreateUniqueRecentAsync(string userId, string type, string title, string body, string? linkUrl = null, int withinHours = 24)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            var threshold = DateTime.UtcNow.AddHours(-Math.Max(1, withinHours));
            var exists = await _context.UserNotifications.AnyAsync(notification =>
                notification.UserId == userId &&
                notification.Type == type &&
                notification.Title == title &&
                notification.CreatedAt >= threshold);

            if (exists)
            {
                return;
            }

            await CreateAsync(userId, type, title, body, linkUrl);
        }
    }
}
