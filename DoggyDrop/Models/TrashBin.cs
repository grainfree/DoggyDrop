using System;

namespace DoggyDrop.Models
{
    public class TrashBin
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;

        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime DateAdded { get; set; }
        public bool IsApproved { get; set; }
        public string? ImageUrl { get; set; } // povezava do slike koša
        public string? UserId { get; set; }


    }
}
