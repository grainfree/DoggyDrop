using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class PlannedWalkStop
    {
        public int Id { get; set; }

        public int PlannedWalkId { get; set; }

        [ForeignKey(nameof(PlannedWalkId))]
        public PlannedWalk? PlannedWalk { get; set; }

        public int Order { get; set; }

        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(40)]
        public string Type { get; set; } = string.Empty;

        [MaxLength(80)]
        public string Label { get; set; } = string.Empty;

        [MaxLength(240)]
        public string Reason { get; set; } = string.Empty;

        public double Latitude { get; set; }

        public double Longitude { get; set; }
    }
}
