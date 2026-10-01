using System.ComponentModel.DataAnnotations;
using DoggyDrop.Models;
using DoggyDrop.Services;

namespace DoggyDrop.ViewModels;

public sealed class DataSourceInput : IValidatableObject
{
    [Required, StringLength(160)] public string Name { get; set; } = string.Empty;
    public DataSourceType Type { get; set; }
    [StringLength(500)] public string? WebsiteUrl { get; set; }
    [StringLength(120)] public string? ContactName { get; set; }
    [StringLength(254), EmailAddress] public string? ContactEmail { get; set; }
    [StringLength(2000)] public string? Notes { get; set; }
    public DateOnly? DataDate { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!Enum.IsDefined(Type)) yield return new("Izberi veljaven tip vira.", [nameof(Type)]);
        if (!string.IsNullOrWhiteSpace(WebsiteUrl) && PlaceLinks.SafeWebsite(WebsiteUrl) == null)
            yield return new("Vnesi veljavno povezavo HTTP ali HTTPS.", [nameof(WebsiteUrl)]);
    }

    public void ApplyTo(DataSource source)
    {
        source.Name = Name.Trim(); source.Type = Type;
        source.WebsiteUrl = Clean(WebsiteUrl); source.ContactName = Clean(ContactName);
        source.ContactEmail = Clean(ContactEmail); source.Notes = Clean(Notes); source.DataDate = DataDate;
        source.UpdatedAt = DateTime.UtcNow;
    }
    public static DataSourceInput From(DataSource source) => new()
    {
        Name = source.Name, Type = source.Type, WebsiteUrl = source.WebsiteUrl,
        ContactName = source.ContactName, ContactEmail = source.ContactEmail, Notes = source.Notes, DataDate = source.DataDate
    };
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record SourceOption(int Id, string Name);
public sealed record DataSourceRow(int Id, string Name, DataSourceType Type, DateOnly? DataDate, DateTime UpdatedAt, int Bins, int Places);
public sealed record AdminBinRow(int Id, string Name, bool IsApproved, string? SourceName, bool IsRetired = false, bool IsRejected = false);

public enum BulkTarget { Bins = 1, Places = 2 }
public enum BulkAction { Activate = 1, Deactivate = 2, AssignSource = 3, ClearSource = 4 }
public sealed class BulkInput
{
    public BulkTarget Target { get; set; }
    public BulkAction Action { get; set; }
    public int[] Ids { get; set; } = [];
    public int? DataSourceId { get; set; }
}
public sealed record BulkRecord(int Id, string Name);
public sealed record BulkPreview(BulkInput Input, IReadOnlyList<BulkRecord> Records, string? SourceName, string Token = "");
public sealed record BulkTicket(BulkInput Input, string UserId, DateTime ExpiresAt);

public static class DataAdminLabels
{
    public static string SourceType(DataSourceType type) => type switch
    {
        DataSourceType.Manual => "Ročno urejanje", DataSourceType.Municipality => "Občina",
        DataSourceType.UtilityCompany => "Komunalno podjetje", DataSourceType.PublicDataset => "Javna zbirka",
        DataSourceType.Partner => "Partner", DataSourceType.UserContribution => "Prispevki uporabnikov", _ => "Neznano"
    };
    public static string Action(BulkAction action) => action switch
    {
        BulkAction.Activate => "Aktiviraj", BulkAction.Deactivate => "Deaktiviraj",
        BulkAction.AssignSource => "Dodeli vir", BulkAction.ClearSource => "Odstrani povezavo z virom", _ => "Neznano"
    };
}
