using Microsoft.EntityFrameworkCore;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace DoggyDrop.Data
{
    public class ApplicationDbContext : IdentityDbContext

    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<TrashBin> TrashBins { get; set; }
    }
}
