using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;
namespace DoggyDrop.ViewModels;

public sealed class WaterPointInput : IValidatableObject
{
    [MaxLength(200)] public string? Name { get; set; }
    [Required] public double? Latitude { get; set; }
    [Required] public double? Longitude { get; set; }
    public int? DataSourceId { get; set; }
    public bool IsApproved { get; set; }
    public DateTime? OriginalUpdatedAt { get; set; }
    public WaterPotability Potability { get; set; }
    public WaterAccess Access { get; set; }
    public WaterSeasonality Seasonality { get; set; }
    public WaterDogAccess DogAccess { get; set; }
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (Latitude is not double lat || !double.IsFinite(lat) || lat is < -90 or > 90)
            yield return new("Vnesi veljavno širino WGS84.", [nameof(Latitude)]);
        if (Longitude is not double lon || !double.IsFinite(lon) || lon is < -180 or > 180)
            yield return new("Vnesi veljavno dolžino WGS84.", [nameof(Longitude)]);
        if (Name?.Any(char.IsControl) == true) yield return new("Ime vsebuje kontrolne znake.", [nameof(Name)]);
        if (!Enum.IsDefined(Potability) || !Enum.IsDefined(Access) || !Enum.IsDefined(Seasonality) || !Enum.IsDefined(DogAccess))
            yield return new("Izberi veljavne podatke o vodi in dostopu.");
        if (IsApproved && (Potability != WaterPotability.SourceReportedDrinking || Access == WaterAccess.Restricted))
            yield return new("Odobritev zahteva vir za pitno vodo brez omejenega dostopa.");
    }
    public void ApplyTo(WaterPoint p)
    {
        p.Name = string.IsNullOrWhiteSpace(Name) ? null : Name.Trim();
        p.Latitude = Latitude!.Value; p.Longitude = Longitude!.Value;
        p.DataSourceId = DataSourceId; p.Potability = Potability; p.Access = Access;
        p.Seasonality = Seasonality; p.DogAccess = DogAccess; p.IsApproved = IsApproved;
    }
    public static WaterPointInput From(WaterPoint p) => new() { Name=p.Name, Latitude=p.Latitude, Longitude=p.Longitude,
        DataSourceId=p.DataSourceId, IsApproved=p.IsApproved, OriginalUpdatedAt=p.UpdatedAt,
        Potability=p.Potability, Access=p.Access, Seasonality=p.Seasonality, DogAccess=p.DogAccess };
}
