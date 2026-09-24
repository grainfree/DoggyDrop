using System.ComponentModel.DataAnnotations;

namespace DoggyDrop.Models;

public enum PlaceCategory
{
    Veterinarian = 1,
    PetShop = 2
}

public sealed class Place
{
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    public PlaceCategory Category { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    [MaxLength(180)]
    public string? Address { get; set; }

    [MaxLength(40)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? WebsiteUrl { get; set; }

    [MaxLength(240)]
    public string? OpeningHours { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
