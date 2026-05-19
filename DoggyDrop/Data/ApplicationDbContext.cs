using Microsoft.EntityFrameworkCore;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace DoggyDrop.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>

    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<TrashBin> TrashBins { get; set; }

        public DbSet<Dog> Dogs { get; set; }

        public DbSet<Walk> Walks { get; set; }

        public DbSet<WalkPoint> WalkPoints { get; set; }

        public DbSet<PlaydateRequest> PlaydateRequests { get; set; }

        public DbSet<PlaydateInterest> PlaydateInterests { get; set; }

        public DbSet<Friendship> Friendships { get; set; }

        public DbSet<UserNotification> UserNotifications { get; set; }

        public DbSet<WalkReaction> WalkReactions { get; set; }

        public DbSet<WalkComment> WalkComments { get; set; }

        public DbSet<DogParkVisit> DogParkVisits { get; set; }

        public DbSet<PlannedWalk> PlannedWalks { get; set; }

        public DbSet<PlannedWalkStop> PlannedWalkStops { get; set; }

        public DbSet<PlannedWalkRoutePoint> PlannedWalkRoutePoints { get; set; }

        public DbSet<WalkStopCompletion> WalkStopCompletions { get; set; }

        public DbSet<WalkPhoto> WalkPhotos { get; set; }

        public DbSet<WalkPhotoReaction> WalkPhotoReactions { get; set; }

        public DbSet<UserGamificationProfile> UserGamificationProfiles { get; set; }

        public DbSet<UserXpEvent> UserXpEvents { get; set; }

        public DbSet<UserStreak> UserStreaks { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<Friendship>()
                .HasOne(f => f.Requester)
                .WithMany(u => u.SentFriendships)
                .HasForeignKey(f => f.RequesterId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Friendship>()
                .HasOne(f => f.Addressee)
                .WithMany(u => u.ReceivedFriendships)
                .HasForeignKey(f => f.AddresseeId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.Entity<Friendship>()
                .HasIndex(f => new { f.RequesterId, f.AddresseeId })
                .IsUnique();

            builder.Entity<UserNotification>()
                .HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkReaction>()
                .HasOne(reaction => reaction.Walk)
                .WithMany(walk => walk.Reactions)
                .HasForeignKey(reaction => reaction.WalkId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkReaction>()
                .HasOne(reaction => reaction.User)
                .WithMany(user => user.WalkReactions)
                .HasForeignKey(reaction => reaction.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkReaction>()
                .HasIndex(reaction => new { reaction.WalkId, reaction.UserId })
                .IsUnique();

            builder.Entity<WalkComment>()
                .HasOne(comment => comment.Walk)
                .WithMany(walk => walk.Comments)
                .HasForeignKey(comment => comment.WalkId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkComment>()
                .HasOne(comment => comment.User)
                .WithMany(user => user.WalkComments)
                .HasForeignKey(comment => comment.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkPhoto>()
                .HasOne(photo => photo.Walk)
                .WithMany(walk => walk.Photos)
                .HasForeignKey(photo => photo.WalkId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkPhoto>()
                .HasOne(photo => photo.User)
                .WithMany(user => user.WalkPhotos)
                .HasForeignKey(photo => photo.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkPhoto>()
                .HasIndex(photo => new { photo.WalkId, photo.CreatedAt });

            builder.Entity<WalkPhoto>()
                .HasOne(photo => photo.PlannedWalkStop)
                .WithMany()
                .HasForeignKey(photo => photo.PlannedWalkStopId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<WalkPhotoReaction>()
                .HasOne(reaction => reaction.WalkPhoto)
                .WithMany(photo => photo.Reactions)
                .HasForeignKey(reaction => reaction.WalkPhotoId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkPhotoReaction>()
                .HasOne(reaction => reaction.User)
                .WithMany(user => user.WalkPhotoReactions)
                .HasForeignKey(reaction => reaction.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkPhotoReaction>()
                .HasIndex(reaction => new { reaction.WalkPhotoId, reaction.UserId })
                .IsUnique();

            builder.Entity<DogParkVisit>()
                .HasOne(visit => visit.Dog)
                .WithMany(dog => dog.ParkVisits)
                .HasForeignKey(visit => visit.DogId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DogParkVisit>()
                .HasOne(visit => visit.User)
                .WithMany(user => user.DogParkVisits)
                .HasForeignKey(visit => visit.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<DogParkVisit>()
                .HasIndex(visit => new { visit.DogId, visit.PlaceKey, visit.VisitedAt });

            builder.Entity<PlannedWalk>()
                .HasOne(plan => plan.Owner)
                .WithMany(user => user.PlannedWalks)
                .HasForeignKey(plan => plan.OwnerId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PlannedWalk>()
                .HasOne(plan => plan.Dog)
                .WithMany(dog => dog.PlannedWalks)
                .HasForeignKey(plan => plan.DogId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<PlannedWalkStop>()
                .HasOne(stop => stop.PlannedWalk)
                .WithMany(plan => plan.Stops)
                .HasForeignKey(stop => stop.PlannedWalkId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PlannedWalkStop>()
                .HasIndex(stop => new { stop.PlannedWalkId, stop.Order });

            builder.Entity<PlannedWalkRoutePoint>()
                .HasOne(point => point.PlannedWalk)
                .WithMany(plan => plan.RoutePoints)
                .HasForeignKey(point => point.PlannedWalkId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<PlannedWalkRoutePoint>()
                .HasIndex(point => new { point.PlannedWalkId, point.Order });

            builder.Entity<Walk>()
                .HasOne(walk => walk.PlannedWalk)
                .WithMany()
                .HasForeignKey(walk => walk.PlannedWalkId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.Entity<WalkStopCompletion>()
                .HasOne(completion => completion.Walk)
                .WithMany(walk => walk.StopCompletions)
                .HasForeignKey(completion => completion.WalkId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkStopCompletion>()
                .HasOne(completion => completion.PlannedWalkStop)
                .WithMany()
                .HasForeignKey(completion => completion.PlannedWalkStopId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkStopCompletion>()
                .HasOne(completion => completion.User)
                .WithMany(user => user.WalkStopCompletions)
                .HasForeignKey(completion => completion.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<WalkStopCompletion>()
                .HasIndex(completion => new { completion.WalkId, completion.PlannedWalkStopId })
                .IsUnique();

            builder.Entity<UserGamificationProfile>()
                .HasOne(profile => profile.User)
                .WithOne(user => user.GamificationProfile)
                .HasForeignKey<UserGamificationProfile>(profile => profile.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserGamificationProfile>()
                .HasIndex(profile => profile.UserId)
                .IsUnique();

            builder.Entity<UserXpEvent>()
                .HasOne(xpEvent => xpEvent.User)
                .WithMany(user => user.XpEvents)
                .HasForeignKey(xpEvent => xpEvent.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserXpEvent>()
                .HasIndex(xpEvent => new { xpEvent.UserId, xpEvent.OccurredAt });

            builder.Entity<UserXpEvent>()
                .HasIndex(xpEvent => new { xpEvent.UserId, xpEvent.ActivityType, xpEvent.ReferenceType, xpEvent.ReferenceId });

            builder.Entity<UserStreak>()
                .HasOne(streak => streak.User)
                .WithMany(user => user.Streaks)
                .HasForeignKey(streak => streak.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.Entity<UserStreak>()
                .HasIndex(streak => new { streak.UserId, streak.StreakType })
                .IsUnique();
        }
    }
}
