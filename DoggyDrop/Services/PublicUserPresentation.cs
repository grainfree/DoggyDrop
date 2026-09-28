using DoggyDrop.Models;

namespace DoggyDrop.Services;

public static class PublicUserPresentation
{
    // Account identifiers are private; never use Email/UserName (or their local part).
    public static string Name(ApplicationUser? user) =>
        string.IsNullOrWhiteSpace(user?.DisplayName) ? "Uporabnik" : user.DisplayName;
}
