using DoggyDrop.Models;

namespace DoggyDrop.Data
{
    public static class DbInitializer
    {
        public static void Seed(ApplicationDbContext context)
        {
            if (context.TrashBins.Any())
                return; // Če že obstajajo, ne dodaj ponovno

            var bins = new TrashBin[]
            {
                new TrashBin
                {
                    Name = "Koš pri mestnem parku",
                    Latitude = 46.3924,
                    Longitude = 15.5742,
                    DateAdded = DateTime.Now,
                    IsApproved = true
                },
                new TrashBin
                {
                    Name = "Koš pri avtobusni postaji",
                    Latitude = 46.3910,
                    Longitude = 15.5775,
                    DateAdded = DateTime.Now,
                    IsApproved = true
                },
                new TrashBin
                {
                    Name = "Koš na Tomažičevi ulici",
                    Latitude = 46.3901,
                    Longitude = 15.5698,
                    DateAdded = DateTime.Now,
                    IsApproved = true
                }
            };

            context.TrashBins.AddRange(bins);
            context.SaveChanges();
        }
    }
}
