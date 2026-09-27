using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models;

public enum DataSourceType
{
    Manual = 1,
    Municipality = 2,
    UtilityCompany = 3,
    PublicDataset = 4,
    Partner = 5,
    UserContribution = 6
}

// Record origin, not ownership, sponsorship, a user account, or a license.
public sealed class DataSource
{
    public int Id { get; set; }
    [MaxLength(160)] public string Name { get; set; } = string.Empty;
    public DataSourceType Type { get; set; }
    [MaxLength(500)] public string? WebsiteUrl { get; set; }
    [MaxLength(120)] public string? ContactName { get; set; }
    [MaxLength(254)] public string? ContactEmail { get; set; }
    [MaxLength(2000)] public string? Notes { get; set; }
    public DateOnly? DataDate { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
