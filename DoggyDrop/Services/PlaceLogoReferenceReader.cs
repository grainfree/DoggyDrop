using DoggyDrop.Data;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public interface IPlaceLogoReferenceReader
{
    Task<bool> IsReferencedAsync(string logoUrl);
}

public sealed class PlaceLogoReferenceReader(DbContextOptions<ApplicationDbContext> options) : IPlaceLogoReferenceReader
{
    public async Task<bool> IsReferencedAsync(string logoUrl)
    {
        await using var freshContext = new ApplicationDbContext(options);
        return await freshContext.Places.AsNoTracking().AnyAsync(place => place.LogoUrl == logoUrl);
    }
}
