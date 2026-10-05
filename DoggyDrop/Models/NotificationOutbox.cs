using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models;

public enum ActivityEmailType { BinApproved = 1, BinRejected, PhotoApproved, PhotoRejected, LocationApproved, LocationRejected, IssueApproved, IssueRejected }
public enum EmailDeliveryStatus { Pending = 1, Processing, Sent, Failed, Suppressed }
public enum EmailFailure { None = 0, Transient, Recipient, Configuration, Rendering, Disabled, Suppressed, AttemptsExhausted }

public sealed class NotificationPreference
{
    [MaxLength(450)] public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;
    public bool ContributionUpdates { get; set; } = true;
}

public sealed class NotificationOutbox
{
    public long Id { get; set; }
    [MaxLength(450)] public string RecipientUserId { get; set; } = "";
    public ApplicationUser RecipientUser { get; set; } = null!;
    public ActivityEmailType Type { get; set; }
    public int PayloadVersion { get; set; } = 1;
    public int? BinId { get; set; }
    public TrashBin? Bin { get; set; }
    public long? ContributionId { get; set; }
    public BinContribution? Contribution { get; set; }
    [MaxLength(100)] public string EventKey { get; set; } = "";
    public EmailDeliveryStatus Status { get; set; } = EmailDeliveryStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int AttemptCount { get; set; }
    public int AttemptLimit { get; set; } = 5;
    public Guid? LeaseToken { get; set; }
    public DateTime? LeaseUntil { get; set; }
    public DateTime? SentAt { get; set; }
    public EmailFailure Failure { get; set; }
}
