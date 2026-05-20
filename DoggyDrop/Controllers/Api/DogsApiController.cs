using DoggyDrop.Data;
using DoggyDrop.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Controllers.Api
{
    [Authorize]
    [ApiController]
    [Route("api/dogs")]
    public class DogsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DogsApiController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        [HttpGet("my")]
        public async Task<IActionResult> MyDogs()
        {
            var userId = _userManager.GetUserId(User);
            var dogs = await _context.Dogs
                .Where(dog => dog.OwnerId == userId)
                .OrderBy(dog => dog.Name)
                .Select(dog => new
                {
                    dog.Id,
                    dog.Name,
                    dog.Breed,
                    dog.AgeYears,
                    dog.Gender,
                    dog.Size,
                    dog.Character,
                    dog.PhotoUrl,
                    dog.MapIconKey,
                    dog.NearbyVisibility,
                    dog.LastKnownLatitude,
                    dog.LastKnownLongitude
                })
                .ToListAsync();

            return Ok(dogs);
        }

        [HttpGet("{id:int}/parks")]
        public async Task<IActionResult> FavoriteParks(int id)
        {
            var userId = _userManager.GetUserId(User);
            var dogExists = await _context.Dogs.AnyAsync(dog => dog.Id == id && dog.OwnerId == userId);
            if (!dogExists)
            {
                return NotFound();
            }

            var visits = await _context.DogParkVisits
                .Where(visit => visit.DogId == id && visit.UserId == userId)
                .ToListAsync();

            var favoriteParks = visits
                .GroupBy(visit => new
                {
                    visit.PlaceKey,
                    visit.ParkName,
                    visit.Area,
                    visit.Address,
                    visit.Latitude,
                    visit.Longitude
                })
                .Select(group => new
                {
                    group.Key.PlaceKey,
                    group.Key.ParkName,
                    group.Key.Area,
                    group.Key.Address,
                    group.Key.Latitude,
                    group.Key.Longitude,
                    VisitCount = group.Count(),
                    LastVisitedAt = group.Max(visit => visit.VisitedAt)
                })
                .OrderByDescending(park => park.VisitCount)
                .ThenByDescending(park => park.LastVisitedAt)
                .ToList();

            return Ok(new
            {
                DogId = id,
                TotalVisits = visits.Count,
                UniqueParks = favoriteParks.Count,
                FavoriteParks = favoriteParks
            });
        }

        [AllowAnonymous]
        [HttpGet("nearby")]
        public async Task<IActionResult> Nearby(double? latitude, double? longitude, double radiusKm = 10)
        {
            var currentUserId = _userManager.GetUserId(User);
            var acceptedFriendIds = string.IsNullOrWhiteSpace(currentUserId)
                ? new HashSet<string>()
                : await _context.Friendships
                    .Where(friendship =>
                        friendship.Status == "Accepted" &&
                        (friendship.RequesterId == currentUserId || friendship.AddresseeId == currentUserId))
                    .Select(friendship => friendship.RequesterId == currentUserId ? friendship.AddresseeId : friendship.RequesterId)
                    .ToHashSetAsync();

            var dogs = await _context.Dogs
                .Include(dog => dog.Owner)
                .Where(dog =>
                    (dog.NearbyVisibility == "Visible" ||
                     (dog.NearbyVisibility == "FriendsOnly" &&
                      !string.IsNullOrWhiteSpace(currentUserId) &&
                      acceptedFriendIds.Contains(dog.OwnerId))) &&
                    dog.LastKnownLatitude.HasValue &&
                    dog.LastKnownLongitude.HasValue &&
                    dog.OwnerId != currentUserId)
                .Select(dog => new
                {
                    dog.Id,
                    dog.Name,
                    dog.Breed,
                    dog.Size,
                    dog.Character,
                    dog.PhotoUrl,
                    dog.MapIconKey,
                    Latitude = dog.LastKnownLatitude!.Value,
                    Longitude = dog.LastKnownLongitude!.Value,
                    OwnerName = dog.Owner != null && !string.IsNullOrWhiteSpace(dog.Owner.DisplayName)
                        ? dog.Owner.DisplayName
                        : "DoggyDrop uporabnik",
                    dog.LastLocationUpdatedAt,
                    dog.NearbyVisibility
                })
                .ToListAsync();

            if (!latitude.HasValue || !longitude.HasValue)
            {
                return Ok(dogs.Take(50));
            }

            var safeRadiusKm = Math.Clamp(radiusKm, 0.5, 50);
            var nearby = dogs
                .Select(dog => new
                {
                    dog.Id,
                    dog.Name,
                    dog.Breed,
                    dog.Size,
                    dog.Character,
                    dog.PhotoUrl,
                    dog.MapIconKey,
                    dog.Latitude,
                    dog.Longitude,
                    dog.OwnerName,
                    dog.LastLocationUpdatedAt,
                    DistanceKm = GetDistanceKm(latitude.Value, longitude.Value, dog.Latitude, dog.Longitude)
                })
                .Where(dog => dog.DistanceKm <= safeRadiusKm)
                .OrderBy(dog => dog.DistanceKm)
                .Take(50)
                .ToList();

            return Ok(nearby);
        }

        private static double GetDistanceKm(double lat1, double lng1, double lat2, double lng2)
        {
            const double earthRadiusKm = 6371;
            var dLat = ToRadians(lat2 - lat1);
            var dLng = ToRadians(lng2 - lng1);
            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLng / 2) * Math.Sin(dLng / 2);

            return earthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        private static double ToRadians(double value)
        {
            return value * Math.PI / 180;
        }
    }
}
