using Microsoft.AspNetCore.Identity;
using System.Collections.Generic;

namespace DoggyDrop.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? ProfileImageUrl { get; set; }

        // Navigacijska lastnost za povezavo s koši (TrashBins)
        public ICollection<TrashBin>? TrashBins { get; set; }
    }
}
