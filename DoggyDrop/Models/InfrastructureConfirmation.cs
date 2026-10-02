using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models;

public enum InfrastructureConfirmationType { TrashBinPresent = 1, WaterPointWorking = 2 }

// An accepted, explicit observation. Location/accuracy are transient validation inputs,
// not retained evidence. Source, ownership and infrastructure lifecycle remain separate.
public sealed class InfrastructureConfirmation
{
    public long Id { get; set; }
    public InfrastructureConfirmationType Type { get; set; }
    public int? TrashBinId { get; set; }
    public TrashBin? TrashBin { get; set; }
    public int? WaterPointId { get; set; }
    public WaterPoint? WaterPoint { get; set; }
    [MaxLength(450)] public string? UserId { get; set; }
    public ApplicationUser? User { get; set; }
    public DateTime CreatedAt { get; set; }
    public Guid EvidenceVersion { get; set; }
}
