using DoggyDrop.Models;

namespace DoggyDrop.Services
{
    public interface IMapStampService
    {
        MapStampCollection BuildCollection(IReadOnlyList<DogParkVisit> parkVisits);
    }
}
