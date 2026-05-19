using System.ComponentModel.DataAnnotations.Schema;

namespace DoggyDrop.Models
{
    public class PlannedWalkRoutePoint
    {
        public int Id { get; set; }

        public int PlannedWalkId { get; set; }

        [ForeignKey(nameof(PlannedWalkId))]
        public PlannedWalk? PlannedWalk { get; set; }

        public int Order { get; set; }

        public double Latitude { get; set; }

        public double Longitude { get; set; }
    }
}
