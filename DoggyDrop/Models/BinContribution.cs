using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models;

public enum BinContributionType { Issue = 1, Photo = 2 }
public enum BinContributionStatus { Pending = 1, Approved = 2, Rejected = 3 }
public enum BinIssueReason { BIN_MISSING = 1, WRONG_LOCATION = 2, DAMAGED = 3, NOT_PUBLIC = 4, DUPLICATE = 5, OTHER = 6 }

// Evidence and review history, never a replacement for TrashBin source or ownership.
public sealed class BinContribution
{
    public long Id { get; set; }
    public int BinId { get; set; }
    public TrashBin Bin { get; set; } = null!;
    public BinContributionType Type { get; set; }
    public BinContributionStatus Status { get; set; } = BinContributionStatus.Pending;
    public BinIssueReason? Reason { get; set; }
    [MaxLength(1000)] public string? Description { get; set; }
    public double? ProposedLatitude { get; set; }
    public double? ProposedLongitude { get; set; }
    public int? PossibleDuplicateBinId { get; set; }
    [MaxLength(1000)] public string? ProposedPhotoUrl { get; set; }
    [MaxLength(450)] public string? SubmittedByUserId { get; set; }
    public ApplicationUser? SubmittedByUser { get; set; }
    [MaxLength(450)] public string? ReviewedByUserId { get; set; }
    public ApplicationUser? ReviewedByUser { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    [MaxLength(1000)] public string? ReviewNote { get; set; }
    [MaxLength(64)] public string BinSnapshot { get; set; } = "";
    public Guid RequestId { get; set; }
}
