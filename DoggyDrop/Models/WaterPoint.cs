using System.ComponentModel.DataAnnotations;
namespace DoggyDrop.Models;

public enum WaterPotability { Unknown, SourceReportedDrinking, NotDrinking }
public enum WaterAccess { Unknown, Public, Permissive, Restricted }
public enum WaterSeasonality { Unknown, Seasonal, SourceReportedYearRound }
public enum WaterDogAccess { Unknown, Allowed, NotAllowed }

// Infrastructure, not ownership, a Place, or an operational water-quality guarantee.
public sealed class WaterPoint
{
    public int Id { get; set; }
    [MaxLength(200)] public string? Name { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public bool IsApproved { get; set; }
    public bool IsRetired { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public Guid EvidenceVersion { get; set; }
    public int? DataSourceId { get; set; }
    [System.Text.Json.Serialization.JsonIgnore] public DataSource? DataSource { get; set; }
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public DateTime? ApprovedAt { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public WaterPotability Potability { get; set; }
    public WaterAccess Access { get; set; }
    public WaterSeasonality Seasonality { get; set; }
    public WaterDogAccess DogAccess { get; set; }
}
