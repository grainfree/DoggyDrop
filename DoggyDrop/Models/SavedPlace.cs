namespace DoggyDrop.Models;

public sealed class SavedPlace
{
    public string UserId { get; set; } = string.Empty;
    public int PlaceId { get; set; }
    public DateTime SavedAt { get; set; } = DateTime.UtcNow;
    public ApplicationUser User { get; set; } = null!;
    public Place Place { get; set; } = null!;
}
