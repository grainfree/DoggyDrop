using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class TrashBin
    {
        public int Id { get; set; }

        [Required]
        public string Name { get; set; } = string.Empty;

        public double Latitude { get; set; }
        public double Longitude { get; set; }

        public string? ImageUrl { get; set; }

        public DateTime DateAdded { get; set; } = DateTime.UtcNow;

        public bool IsApproved { get; set; } = false;

        public int UsedCount { get; set; }

        public int FullReports { get; set; }

        public int MissingReports { get; set; }

        public int UsefulVotes { get; set; }

        public int NotUsefulVotes { get; set; }

        public DateTime? LastUsedAt { get; set; }

        public DateTime? LastReportedAt { get; set; }

        // Povezava na uporabnika
        public string? UserId { get; set; }

        [ForeignKey("UserId")]
        public ApplicationUser? User { get; set; }

        [NotMapped]
        public string? FullImageUrl
        {
            get
            {
                if (string.IsNullOrWhiteSpace(ImageUrl))
                {
                    return null;
                }

                var value = ImageUrl.Trim().Replace('\\', '/');

                if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("/", StringComparison.Ordinal))
                {
                    return value;
                }

                const string wwwrootMarker = "wwwroot/";
                var wwwrootIndex = value.IndexOf(wwwrootMarker, StringComparison.OrdinalIgnoreCase);
                if (wwwrootIndex >= 0)
                {
                    value = value[(wwwrootIndex + wwwrootMarker.Length)..];
                }

                if (value.StartsWith("uploads/", StringComparison.OrdinalIgnoreCase) ||
                    value.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
                {
                    return "/" + value.TrimStart('/');
                }

                return "/uploads/trashbins/" + value.TrimStart('/');
            }
        }

    }
}
