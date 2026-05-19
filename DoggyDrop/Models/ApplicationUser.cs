using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace DoggyDrop.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? ProfileImageUrl { get; set; }

        public string? DisplayName { get; set; }


        // Navigacijska lastnost za povezavo s koši (TrashBins)
        public ICollection<TrashBin>? TrashBins { get; set; }

        public ICollection<Dog>? Dogs { get; set; }

        public ICollection<Walk>? Walks { get; set; }

        public ICollection<PlaydateRequest>? PlaydateRequests { get; set; }

        public ICollection<Friendship>? SentFriendships { get; set; }

        public ICollection<Friendship>? ReceivedFriendships { get; set; }

        public ICollection<UserNotification>? Notifications { get; set; }

        public ICollection<WalkReaction>? WalkReactions { get; set; }

        public ICollection<WalkComment>? WalkComments { get; set; }

        public ICollection<WalkPhoto>? WalkPhotos { get; set; }

        public ICollection<WalkPhotoReaction>? WalkPhotoReactions { get; set; }

        public ICollection<DogParkVisit>? DogParkVisits { get; set; }

        public ICollection<PlannedWalk>? PlannedWalks { get; set; }

        public ICollection<WalkStopCompletion>? WalkStopCompletions { get; set; }

        public UserGamificationProfile? GamificationProfile { get; set; }

        public ICollection<UserXpEvent>? XpEvents { get; set; }

        public ICollection<UserStreak>? Streaks { get; set; }
    }
}
