using Microsoft.AspNetCore.Identity;

namespace DoggyDrop.Models
{
    public class ApplicationUser : IdentityUser
    {
        public string? ProfileImageUrl { get; set; }
    }
}
