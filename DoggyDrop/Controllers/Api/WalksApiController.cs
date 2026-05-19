using DoggyDrop.Data;
using DoggyDrop.Models;
using DoggyDrop.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [Authorize]
    [ApiController]
    [Route("api/walks")]
    public class WalksApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly INotificationService _notificationService;

        public WalksApiController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            INotificationService notificationService)
        {
            _context = context;
            _userManager = userManager;
            _notificationService = notificationService;
        }

        [HttpGet("stats")]
        public async Task<IActionResult> Stats()
        {
            var userId = _userManager.GetUserId(User);
            var weekStart = DateTime.UtcNow.Date.AddDays(-6);

            var walks = await _context.Walks
                .Where(walk => walk.OwnerId == userId && walk.Status == "Completed")
                .ToListAsync();

            var totalDuration = TimeSpan.FromTicks(walks
                .Where(walk => walk.EndedAt.HasValue)
                .Sum(walk => (walk.EndedAt!.Value - walk.StartedAt).Ticks));

            return Ok(new
            {
                TotalWalks = walks.Count,
                WalksThisWeek = walks.Count(walk => walk.StartedAt >= weekStart),
                TotalDistanceKm = walks.Sum(walk => walk.DistanceMeters) / 1000,
                TotalDurationMinutes = Math.Round(totalDuration.TotalMinutes),
                UsedBinsCount = walks.Sum(walk => walk.UsedBinsCount)
            });
        }

        [HttpGet("recent")]
        public async Task<IActionResult> Recent()
        {
            var userId = _userManager.GetUserId(User);
            var walks = await _context.Walks
                .Include(walk => walk.Dog)
                .Include(walk => walk.PlannedWalk)
                .Include(walk => walk.Reactions)
                .Include(walk => walk.Comments)
                .Include(walk => walk.Photos)
                .Where(walk => walk.OwnerId == userId && walk.Status == "Completed")
                .OrderByDescending(walk => walk.StartedAt)
                .Take(20)
                .Select(walk => new
                {
                    walk.Id,
                    DogId = walk.DogId,
                    DogName = walk.Dog != null ? walk.Dog.Name : "Pes",
                    walk.StartedAt,
                    walk.EndedAt,
                    DistanceKm = walk.DistanceMeters / 1000,
                    walk.UsedBinsCount,
                    PlannedWalkId = walk.PlannedWalkId,
                    PlannedWalkTitle = walk.PlannedWalk != null ? walk.PlannedWalk.Title : null,
                    LikeCount = walk.Reactions != null ? walk.Reactions.Count : 0,
                    CommentCount = walk.Comments != null ? walk.Comments.Count(comment => !comment.IsDeleted) : 0,
                    PhotoCount = walk.Photos != null ? walk.Photos.Count : 0,
                    CoverPhotoUrl = walk.Photos != null
                        ? walk.Photos.OrderByDescending(photo => photo.CreatedAt).Select(photo => photo.ImageUrl).FirstOrDefault()
                        : null
                })
                .ToListAsync();

            return Ok(walks);
        }

        [HttpGet("plans")]
        public async Task<IActionResult> Plans()
        {
            var userId = _userManager.GetUserId(User);
            var plans = await _context.PlannedWalks
                .Include(plan => plan.Dog)
                .Include(plan => plan.Stops)
                .Include(plan => plan.RoutePoints)
                .Where(plan => plan.OwnerId == userId)
                .OrderByDescending(plan => plan.CreatedAt)
                .Take(30)
                .Select(plan => new
                {
                    plan.Id,
                    plan.Title,
                    plan.AreaKey,
                    plan.AreaName,
                    plan.TargetDistanceKm,
                    plan.EstimatedDistanceKm,
                    plan.EstimatedMinutes,
                    plan.CreatedAt,
                    plan.UsedAt,
                    Dog = plan.Dog == null ? null : new
                    {
                        plan.Dog.Id,
                        plan.Dog.Name,
                        plan.Dog.PhotoUrl
                    },
                    StopCount = plan.Stops != null ? plan.Stops.Count : 0,
                    RoutePointCount = plan.RoutePoints != null ? plan.RoutePoints.Count : 0
                })
                .ToListAsync();

            return Ok(plans);
        }

        [HttpGet("plans/{id:int}")]
        public async Task<IActionResult> Plan(int id)
        {
            var userId = _userManager.GetUserId(User);
            var plan = await _context.PlannedWalks
                .Include(item => item.Dog)
                .Include(item => item.Stops)
                .Include(item => item.RoutePoints)
                .FirstOrDefaultAsync(item => item.Id == id && item.OwnerId == userId);

            if (plan == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                plan.Id,
                plan.Title,
                plan.AreaKey,
                plan.AreaName,
                plan.TargetDistanceKm,
                plan.EstimatedDistanceKm,
                plan.EstimatedMinutes,
                plan.IncludeBins,
                plan.IncludePark,
                plan.IncludeWater,
                plan.IncludeDogFriendly,
                plan.CreatedAt,
                plan.UsedAt,
                Dog = plan.Dog == null ? null : new
                {
                    plan.Dog.Id,
                    plan.Dog.Name,
                    plan.Dog.PhotoUrl
                },
                Stops = (plan.Stops ?? [])
                    .OrderBy(stop => stop.Order)
                    .Select(stop => new
                    {
                        stop.Order,
                        stop.Name,
                        stop.Type,
                        stop.Label,
                        stop.Reason,
                        stop.Latitude,
                        stop.Longitude
                    }),
                RoutePoints = (plan.RoutePoints ?? [])
                    .OrderBy(point => point.Order)
                    .Select(point => new
                    {
                        point.Order,
                        point.Latitude,
                        point.Longitude
                    })
            });
        }

        [HttpGet("{id:int}/social")]
        public async Task<IActionResult> Social(int id)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks
                .Include(walk => walk.Reactions)
                .Include(walk => walk.Comments!.Where(comment => !comment.IsDeleted))
                    .ThenInclude(comment => comment.User)
                .Include(walk => walk.Photos)
                .FirstOrDefaultAsync(walk => walk.Id == id && walk.Status == "Completed");

            if (walk == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                walk.Id,
                LikeCount = walk.Reactions?.Count ?? 0,
                CommentCount = walk.Comments?.Count ?? 0,
                IsLikedByCurrentUser = walk.Reactions?.Any(reaction => reaction.UserId == userId) ?? false,
                Comments = (walk.Comments ?? [])
                    .OrderByDescending(comment => comment.CreatedAt)
                    .Take(20)
                    .Select(comment => new
                    {
                        comment.Id,
                        Author = GetDisplayName(comment.User),
                        comment.Body,
                        comment.CreatedAt
                    }),
                Photos = (walk.Photos ?? [])
                    .OrderByDescending(photo => photo.CreatedAt)
                    .Select(photo => new
                    {
                        photo.Id,
                        photo.ImageUrl,
                        photo.Caption,
                        photo.CreatedAt
                    })
            });
        }

        [HttpGet("{id:int}/photos")]
        public async Task<IActionResult> Photos(int id)
        {
            var userId = _userManager.GetUserId(User);
            var walk = await _context.Walks
                .Include(item => item.Photos!)
                    .ThenInclude(photo => photo.Reactions)
                .Include(item => item.Photos!)
                    .ThenInclude(photo => photo.PlannedWalkStop)
                .FirstOrDefaultAsync(item => item.Id == id && (item.OwnerId == userId || item.Status == "Completed"));

            if (walk == null)
            {
                return NotFound();
            }

            return Ok(new
            {
                walk.Id,
                Photos = (walk.Photos ?? [])
                    .OrderByDescending(photo => photo.CreatedAt)
                    .Select(photo => new
                    {
                        photo.Id,
                        photo.ImageUrl,
                        photo.Caption,
                        photo.CreatedAt,
                        ReactionCount = photo.Reactions != null ? photo.Reactions.Count : 0,
                        IsReactedByCurrentUser = photo.Reactions != null && photo.Reactions.Any(reaction => reaction.UserId == userId),
                        StopName = photo.PlannedWalkStop != null ? photo.PlannedWalkStop.Name : null
                    })
            });
        }

        [HttpPost("photos/{photoId:int}/like")]
        public async Task<IActionResult> TogglePhotoLike(int photoId, [FromQuery] string? reactionType = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var photo = await _context.WalkPhotos
                .Include(item => item.Walk)
                .FirstOrDefaultAsync(item => item.Id == photoId && item.Walk != null && item.Walk.Status == "Completed");

            if (photo == null)
            {
                return NotFound();
            }

            var normalizedReaction = NormalizeReactionType(reactionType, "heart");
            var reaction = await _context.WalkPhotoReactions
                .FirstOrDefaultAsync(existing => existing.WalkPhotoId == photoId && existing.UserId == userId);
            var isLiked = reaction == null;

            if (reaction == null)
            {
                _context.WalkPhotoReactions.Add(new WalkPhotoReaction
                {
                    WalkPhotoId = photoId,
                    UserId = userId,
                    ReactionType = normalizedReaction,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else if (reaction.ReactionType != normalizedReaction)
            {
                reaction.ReactionType = normalizedReaction;
                reaction.CreatedAt = DateTime.UtcNow;
                isLiked = true;
            }
            else
            {
                _context.WalkPhotoReactions.Remove(reaction);
            }

            await _context.SaveChangesAsync();
            var count = await _context.WalkPhotoReactions.CountAsync(existing => existing.WalkPhotoId == photoId);
            var counts = await BuildPhotoReactionCountsAsync(photoId);
            return Ok(new { IsLiked = isLiked, ReactionCount = count, ReactionType = normalizedReaction, Reactions = counts });
        }

        [HttpPost("{id:int}/like")]
        public async Task<IActionResult> ToggleLike(int id, [FromQuery] string? reactionType = null)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var walk = await _context.Walks
                .Include(walk => walk.Dog)
                .FirstOrDefaultAsync(walk => walk.Id == id && walk.Status == "Completed");

            if (walk == null)
            {
                return NotFound();
            }

            var normalizedReaction = NormalizeReactionType(reactionType, "paw");
            var reaction = await _context.WalkReactions
                .FirstOrDefaultAsync(existing => existing.WalkId == id && existing.UserId == userId);
            var isLiked = reaction == null;

            if (reaction == null)
            {
                _context.WalkReactions.Add(new WalkReaction
                {
                    WalkId = id,
                    UserId = userId,
                    ReactionType = normalizedReaction,
                    CreatedAt = DateTime.UtcNow
                });
            }
            else if (reaction.ReactionType != normalizedReaction)
            {
                reaction.ReactionType = normalizedReaction;
                reaction.CreatedAt = DateTime.UtcNow;
                isLiked = true;
            }
            else
            {
                _context.WalkReactions.Remove(reaction);
            }

            await _context.SaveChangesAsync();

            if (isLiked && walk.OwnerId != userId)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                await _notificationService.CreateAsync(
                    walk.OwnerId,
                    "WalkReaction",
                    "Nova reakcija na sprehodu",
                    $"{GetDisplayName(currentUser)} je reagiral na sprehod psa {walk.Dog?.Name ?? "Pes"}.",
                    $"/Walks/Details/{walk.Id}");
            }

            var likeCount = await _context.WalkReactions.CountAsync(existing => existing.WalkId == id);
            var counts = await BuildWalkReactionCountsAsync(id);
            return Ok(new { IsLiked = isLiked, LikeCount = likeCount, ReactionType = normalizedReaction, Reactions = counts });
        }

        [HttpPost("{id:int}/comments")]
        public async Task<IActionResult> AddComment(int id, [FromBody] WalkCommentInput input)
        {
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId))
            {
                return Challenge();
            }

            var walk = await _context.Walks
                .Include(walk => walk.Dog)
                .FirstOrDefaultAsync(walk => walk.Id == id && walk.Status == "Completed");

            if (walk == null)
            {
                return NotFound();
            }

            var body = input.Body?.Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                return BadRequest(new { Message = "Komentar ne sme biti prazen." });
            }

            if (body.Length > 240)
            {
                body = body[..240];
            }

            var comment = new WalkComment
            {
                WalkId = id,
                UserId = userId,
                Body = body,
                CreatedAt = DateTime.UtcNow
            };

            _context.WalkComments.Add(comment);
            await _context.SaveChangesAsync();

            if (walk.OwnerId != userId)
            {
                var currentUser = await _userManager.GetUserAsync(User);
                await _notificationService.CreateAsync(
                    walk.OwnerId,
                    "WalkComment",
                    "Nov komentar",
                    $"{GetDisplayName(currentUser)} je komentiral sprehod psa {walk.Dog?.Name ?? "Pes"}.",
                    $"/Walks/Details/{walk.Id}");
            }

            return Ok(new
            {
                comment.Id,
                comment.Body,
                comment.CreatedAt
            });
        }

        private static string GetDisplayName(ApplicationUser? user)
        {
            if (!string.IsNullOrWhiteSpace(user?.DisplayName))
            {
                return user.DisplayName;
            }

            return user?.Email ?? "DoggyDrop uporabnik";
        }

        private async Task<IReadOnlyDictionary<string, int>> BuildWalkReactionCountsAsync(int walkId)
        {
            return await _context.WalkReactions
                .Where(reaction => reaction.WalkId == walkId)
                .GroupBy(reaction => reaction.ReactionType)
                .ToDictionaryAsync(group => group.Key, group => group.Count());
        }

        private async Task<IReadOnlyDictionary<string, int>> BuildPhotoReactionCountsAsync(int photoId)
        {
            return await _context.WalkPhotoReactions
                .Where(reaction => reaction.WalkPhotoId == photoId)
                .GroupBy(reaction => reaction.ReactionType)
                .ToDictionaryAsync(group => group.Key, group => group.Count());
        }

        private static string NormalizeReactionType(string? reactionType, string fallback)
        {
            return reactionType?.Trim().ToLowerInvariant() switch
            {
                "paw" => "paw",
                "heart" => "heart",
                "fire" => "fire",
                "good-route" => "good-route",
                "cute-dog" => "cute-dog",
                _ => fallback
            };
        }
    }

    public class WalkCommentInput
    {
        public string? Body { get; set; }
    }
}
