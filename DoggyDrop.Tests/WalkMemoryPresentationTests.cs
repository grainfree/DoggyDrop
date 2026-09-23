using DoggyDrop.Models;
using DoggyDrop.Services;
using DoggyDrop.ViewModels;
using Xunit;

namespace DoggyDrop.Tests;

public sealed class WalkMemoryPresentationTests
{
    [Theory]
    [InlineData(2026, 1, 15, 12, 13)]
    [InlineData(2026, 7, 15, 12, 14)]
    public void LocalTime_UsesLjubljanaIncludingDaylightSaving(int year, int month, int day, int utcHour, int localHour)
    {
        var utc = new DateTime(year, month, day, utcHour, 0, 0, DateTimeKind.Utc);

        Assert.Equal(localHour, WalkMemoryPresentation.LocalTime(utc).Hour);
    }

    [Theory]
    [InlineData(0, "0 fotografij")]
    [InlineData(1, "1 fotografija")]
    [InlineData(2, "2 fotografiji")]
    [InlineData(3, "3 fotografije")]
    [InlineData(4, "4 fotografije")]
    [InlineData(5, "5 fotografij")]
    [InlineData(11, "11 fotografij")]
    [InlineData(21, "21 fotografija")]
    public void PhotoCount_UsesSlovenianPlural(int count, string expected) =>
        Assert.Equal(expected, SlovenianFormatting.PhotoCount(count));

    [Fact]
    public void CompletedWalk_UsesActualDistanceAndOnlyItsOwnedPhotos()
    {
        var walk = CompletedWalk();
        walk.DistanceMeters = 4;
        walk.PlannedWalk = new PlannedWalk
        {
            EstimatedDistanceKm = 5,
            RoutePoints =
            [
                new PlannedWalkRoutePoint { Latitude = 46, Longitude = 14 },
                new PlannedWalkRoutePoint { Latitude = 46.01, Longitude = 14.01 }
            ]
        };
        walk.Photos =
        [
            new WalkPhoto { Id = 1, WalkId = walk.Id, UserId = walk.OwnerId, ImageUrl = "https://example.test/owned.jpg", CreatedAt = walk.StartedAt.AddHours(1) },
            new WalkPhoto { Id = 2, WalkId = walk.Id, UserId = walk.OwnerId, ImageUrl = "https://example.test/latest.jpg", CreatedAt = walk.StartedAt.AddHours(1) },
            new WalkPhoto { WalkId = walk.Id, UserId = "other-owner", ImageUrl = "https://example.test/other.jpg", CreatedAt = walk.StartedAt.AddHours(2) },
            new WalkPhoto { WalkId = walk.Id + 1, UserId = walk.OwnerId, ImageUrl = "https://example.test/different-walk.jpg", CreatedAt = walk.StartedAt.AddHours(3) }
        ];

        var memory = WalkMemoryPresentation.Build(walk, [], [], []);

        Assert.Equal("Sprehod s Floyd", memory.Title);
        Assert.Equal("4 m", memory.DistanceLabel);
        Assert.Equal("https://example.test/latest.jpg", memory.HeroPhotoUrl);
        Assert.Equal(2, memory.PhotoCount);
        Assert.False(memory.HasActualTrail);
        Assert.True(memory.HasPlannedRoute);
        Assert.Contains("4 m", memory.ShareText);
        Assert.DoesNotContain("5 km", memory.ShareText);
        Assert.Equal("https://example.test/latest.jpg",
            WalkMemoryPresentation.Build(walk, [], [], [], includeOwnerDetails: true).ShareAsset?.PhotoUrl);
    }

    [Fact]
    public void NoPhotoOrUsefulRoute_HasNeutralFallbackAndNoMap()
    {
        var walk = CompletedWalk();
        walk.Points = [new WalkPoint { Latitude = 46, Longitude = 14 }];

        var memory = WalkMemoryPresentation.Build(walk, [], [], []);

        Assert.Null(memory.HeroPhotoUrl);
        Assert.Equal(0, memory.PhotoCount);
        Assert.False(memory.HasMap);
        Assert.Empty(memory.Highlights);
    }

    [Fact]
    public void UsefulGpsTrail_TakesPriorityOverPlannedRoute()
    {
        var walk = CompletedWalk();
        walk.Points =
        [
            new WalkPoint { Latitude = 46, Longitude = 14 },
            new WalkPoint { Latitude = 46.001, Longitude = 14.001 }
        ];
        walk.PlannedWalk = new PlannedWalk
        {
            RoutePoints =
            [
                new PlannedWalkRoutePoint { Latitude = 46, Longitude = 14 },
                new PlannedWalkRoutePoint { Latitude = 46.02, Longitude = 14.02 }
            ]
        };

        var memory = WalkMemoryPresentation.Build(walk, [], [], []);

        Assert.True(memory.HasActualTrail);
        Assert.True(memory.HasPlannedRoute);
    }

    [Fact]
    public void NormalAndVeryShortGpsCoveredWalks_ShowElapsedDuration()
    {
        var walk = CompletedWalk();
        walk.EndedAt = walk.StartedAt.AddMinutes(30);
        walk.Points = GpsPoints(walk, 0, 15, 30);
        Assert.Equal("30 min", WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);

        walk.EndedAt = walk.StartedAt.AddSeconds(30);
        walk.Points = GpsPoints(walk, 0, 0.5);
        Assert.Equal("30 s", WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);
    }

    [Fact]
    public void LegitimateLongWalkWithGpsCoverage_IsNotRejectedAtTwelveHours()
    {
        var walk = CompletedWalk();
        walk.EndedAt = walk.StartedAt.AddHours(13);
        walk.Points = GpsPoints(walk, Enumerable.Range(0, 14).Select(hour => hour * 60d).ToArray());

        Assert.Equal("13 h 0 min", WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);

        walk.EndedAt = walk.StartedAt.AddHours(26);
        walk.Points = GpsPoints(walk, Enumerable.Range(0, 27).Select(hour => hour * 60d).ToArray());
        Assert.Equal("26 h 0 min", WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);
    }

    [Fact]
    public void HistoricalLateFinishAndLargeGpsGap_HideElapsedDuration()
    {
        var walk = CompletedWalk();
        walk.EndedAt = walk.StartedAt.AddHours(8);
        walk.Points = GpsPoints(walk, 0, 10, 20);
        Assert.Null(WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);

        walk.Points = GpsPoints(walk, 0, 10, 470, 480);
        Assert.Null(WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);

        walk.EndedAt = walk.StartedAt.AddDays(3);
        walk.Points = GpsPoints(walk, 0, 24 * 60, 48 * 60, 72 * 60);
        Assert.Null(WalkMemoryPresentation.Build(walk, [], [], []).DurationLabel);
    }

    [Fact]
    public void NoGpsPoints_HideElapsedDuration()
    {
        Assert.Null(WalkMemoryPresentation.Build(CompletedWalk(), [], [], []).DurationLabel);
    }

    [Fact]
    public void OwnerShare_UsesActualMetricsAndNoPhotoFallbackWithoutPrivatePlanTitle()
    {
        var walk = CompletedWalk();
        walk.DistanceMeters = 4;
        walk.EndedAt = walk.StartedAt.AddMinutes(30);
        walk.Points = GpsPoints(walk, 0, 15, 30);
        walk.PlannedWalk = new PlannedWalk { Title = "Zasebni naslov", EstimatedDistanceKm = 8 };

        var memory = WalkMemoryPresentation.Build(walk,
            [new UserXpEvent { ActivityType = GamificationConstants.WalkDistance, XpAmount = 15 }],
            [], [], includeOwnerDetails: true);
        var share = Assert.IsType<WalkShareAssetViewModel>(memory.ShareAsset);

        Assert.Equal("4 m", share.Distance);
        Assert.Equal("30 min", share.Duration);
        Assert.Equal("+15 XP", share.Highlight);
        Assert.Null(share.PhotoUrl);
        Assert.Contains("4 m", share.Text);
        Assert.DoesNotContain("8 km", share.Text);
        Assert.DoesNotContain("Zasebni naslov", System.Text.Json.JsonSerializer.Serialize(share));

        Assert.Null(WalkMemoryPresentation.Build(walk, [], [], []).ShareAsset);
    }

    [Fact]
    public void OwnerShare_OmitsUntrustworthyDurationAndUnrelatedReward()
    {
        var walk = CompletedWalk();
        walk.EndedAt = walk.StartedAt.AddHours(8);
        walk.Points = GpsPoints(walk, 0, 10, 20);

        var share = Assert.IsType<WalkShareAssetViewModel>(WalkMemoryPresentation.Build(walk,
            [new UserXpEvent { ActivityType = GamificationConstants.UploadPhoto, XpAmount = 50 }],
            [], [], includeOwnerDetails: true).ShareAsset);

        Assert.Null(share.Duration);
        Assert.Null(share.Highlight);
    }

    [Fact]
    public void OnlyDurablyLinkedWalkRewardsAppearOnReopen()
    {
        var walk = CompletedWalk();
        var memory = WalkMemoryPresentation.Build(walk,
            [new UserXpEvent { ActivityType = GamificationConstants.WalkDistance, XpAmount = 30 },
             new UserXpEvent { ActivityType = GamificationConstants.UploadPhoto, XpAmount = 10 }],
            [new DogXpEvent { ActivityType = "CompletedWalk", XpAmount = 20 }],
            [new UserAchievement { AchievementKey = UserAchievementCatalog.WalkFirst, UnlockedAt = walk.EndedAt!.Value }]);

        Assert.Contains(memory.Highlights, item => item.Title == "Tvoje izkušnje" && item.Detail == "+30 XP");
        Assert.Contains(memory.Highlights, item => item.Title == "Pasji napredek" && item.Detail == "+20 XP");
        Assert.Contains(memory.Highlights, item => item.Title == "Odklenjen dosežek");
        Assert.Equal(3, memory.Highlights.Count);
    }

    private static Walk CompletedWalk() => new()
    {
        Id = 7,
        OwnerId = "owner",
        Status = "Completed",
        Dog = new Dog { Name = "Floyd" },
        StartedAt = new DateTime(2026, 9, 23, 15, 0, 0, DateTimeKind.Utc),
        EndedAt = new DateTime(2026, 9, 23, 16, 0, 0, DateTimeKind.Utc)
    };

    private static List<WalkPoint> GpsPoints(Walk walk, params double[] minutes) => minutes
        .Select((minute, index) => new WalkPoint
        {
            WalkId = walk.Id,
            RecordedAt = walk.StartedAt.AddMinutes(minute),
            Latitude = 46 + index * 0.0001,
            Longitude = 14
        }).ToList();
}
