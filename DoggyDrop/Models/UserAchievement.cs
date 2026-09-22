using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models;

public class UserAchievement
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    public ApplicationUser? User { get; set; }

    [Required, MaxLength(80)]
    public string AchievementKey { get; set; } = string.Empty;

    public DateTime UnlockedAt { get; set; }

    [MaxLength(80)]
    public string? SourceType { get; set; }

    [MaxLength(160)]
    public string? SourceId { get; set; }

    public DateTime CreatedAt { get; set; }
}
