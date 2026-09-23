using System.Globalization;
using DoggyDrop.Data;
using DoggyDrop.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace DoggyDrop.Services;

public sealed class WeeklyGoalsService : IWeeklyGoalsService
{
    private static readonly CultureInfo Slovenian = CultureInfo.GetCultureInfo("sl-SI");
    private readonly ApplicationDbContext _context;
    private readonly IGamificationCalendar _calendar;

    public WeeklyGoalsService(ApplicationDbContext context, IGamificationCalendar calendar)
    {
        _context = context;
        _calendar = calendar;
    }

    public async Task<WeeklyGoalsViewModel> GetForUserAsync(string userId)
    {
        var now = _calendar.UtcNow.UtcDateTime;
        var week = WeeklyGoalWeek.At(now);
        // Completion owns the walk's week, including walks started before Monday.
        var walks = _context.Walks.AsNoTracking().Where(walk =>
            walk.OwnerId == userId && walk.Status == "Completed" && walk.EndedAt >= week.StartUtc &&
            walk.EndedAt < week.EndUtc && walk.EndedAt <= now);

        var activity = await walks.GroupBy(walk => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Distance = group.Sum(walk => walk.DistanceMeters >= 0 && walk.DistanceMeters <= double.MaxValue
                    ? walk.DistanceMeters : 0d)
            })
            .FirstOrDefaultAsync();
        var walkCount = activity?.Count ?? 0;
        var totalMeters = activity?.Distance ?? 0;
        var meters = double.IsFinite(totalMeters) ? Math.Max(0, totalMeters) : 0;

        var hasPhoto = await _context.WalkPhotos.AsNoTracking().AnyAsync(photo =>
            photo.UserId == userId && photo.CreatedAt >= week.StartUtc &&
            photo.CreatedAt < week.EndUtc && photo.CreatedAt <= now && photo.Walk != null &&
            photo.Walk.OwnerId == userId && photo.Walk.Status == "Completed" &&
            photo.Walk.EndedAt >= week.StartUtc && photo.Walk.EndedAt < week.EndUtc && photo.Walk.EndedAt <= now);

        var remainingWalks = Math.Max(0, 3 - walkCount);
        var remainingMeters = Math.Max(0, 5000 - meters);
        return new WeeklyGoalsViewModel
        {
            Monday = week.Monday,
            Goals =
            [
                new("walks", "Aktivne tačke", "Opravi 3 sprehode ta teden.", "bi-signpost-split", walkCount, 3,
                    $"{walkCount} / 3", $"Še {WeeklyGoalsWording.Walks(remainingWalks)} do cilja."),
                new("distance", "Kilometri ta teden", "Prehodi 5 km ta teden.", "bi-geo-alt", meters, 5000,
                    DistanceProgress(meters), DistanceRemaining(remainingMeters)),
                new("photo", "Pasji fotograf", "Ta teden dodaj fotografijo k sprehodu, zaključenemu ta teden.", "bi-camera", hasPhoto ? 1 : 0, 1,
                    $"{(hasPhoto ? 1 : 0)} / 1", "Dodaj fotografijo k sprehodu tega tedna.")
            ]
        };
    }

    private static string DistanceProgress(double meters)
    {
        if (meters is > 0 and < 100) return $"{Math.Ceiling(meters).ToString("0", Slovenian)} m / 5 km";
        var format = meters is >= 4900 and < 5000 ? "0.00" : "0.0";
        var displayedKm = format == "0.00" ? Math.Floor(meters / 10d) / 100d : meters / 1000d;
        return $"{displayedKm.ToString(format, Slovenian)} / 5 km";
    }

    private static string DistanceRemaining(double meters) => meters is > 0 and < 100
        ? $"Še {Math.Ceiling(meters).ToString("0", Slovenian)} m do tedenskega cilja."
        : $"Še {(meters / 1000d).ToString("0.0", Slovenian)} km do tedenskega cilja.";
}
