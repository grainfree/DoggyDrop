using System.Security.Claims;
using DoggyDrop.Controllers;
using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class NotificationsControllerTests
{
    [Fact]
    public async Task MarkRead_CannotModifyAnotherUsersNotification()
    {
        var path = Path.Combine(Path.GetTempPath(), $"doggydrop-notifications-{Guid.NewGuid():N}.db");
        try
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite($"Data Source={path};Pooling=False").Options;
            await using var db = new ApplicationDbContext(options);
            await db.Database.EnsureCreatedAsync();
            db.Users.AddRange(
                new ApplicationUser { Id = "owner", UserName = "owner@test" },
                new ApplicationUser { Id = "other", UserName = "other@test" });
            var notification = new UserNotification { UserId = "owner", Title = "Test" };
            db.UserNotifications.Add(notification);
            await db.SaveChangesAsync();

            var manager = new UserManager<ApplicationUser>(
                new UserStore<ApplicationUser, IdentityRole, ApplicationDbContext>(db),
                Options.Create(new IdentityOptions()), new PasswordHasher<ApplicationUser>(),
                [], [], new UpperInvariantLookupNormalizer(), new IdentityErrorDescriber(),
                new ServiceCollection().BuildServiceProvider(), NullLogger<UserManager<ApplicationUser>>.Instance);
            var controller = new NotificationsController(db, manager, null!);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                        [new Claim(ClaimTypes.NameIdentifier, "other")], "Test"))
                }
            };

            Assert.IsType<NotFoundResult>(await controller.MarkRead(notification.Id));
            db.ChangeTracker.Clear();
            Assert.False((await db.UserNotifications.SingleAsync()).IsRead);
            Assert.Null((await db.UserNotifications.SingleAsync()).ReadAt);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
