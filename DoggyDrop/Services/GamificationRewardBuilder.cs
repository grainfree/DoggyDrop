using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public sealed class GamificationRewardBuilder : IGamificationRewardBuilder
{
    public UserRewardViewModel? BuildUserReward(UserXpEvent? xpEvent, GamificationLevelInfo before, GamificationLevelInfo after) =>
        xpEvent == null ? null : new UserRewardViewModel
        {
            XpEarned = xpEvent.XpAmount,
            PreviousXp = before.TotalXp,
            CurrentXp = after.TotalXp,
            PreviousLevel = before.Level,
            CurrentLevel = after.Level,
            LevelTitle = after.Title,
            XpRemaining = after.XpRemaining,
            ProgressPercent = after.ProgressPercent
        };

    public DogRewardViewModel? BuildDogReward(Dog dog, DogXpEvent? xpEvent, DogProgressionLevelInfo beforeLevel, DogProgressionLevelInfo afterLevel, DogProgressionProfile before, DogProgressionProfile after) =>
        xpEvent == null ? null : new DogRewardViewModel
        {
            DogId = dog.Id,
            DogName = dog.Name,
            XpEarned = xpEvent.XpAmount,
            PreviousXp = beforeLevel.TotalXp,
            CurrentXp = afterLevel.TotalXp,
            PreviousLevel = beforeLevel.Level,
            CurrentLevel = afterLevel.Level,
            PreviousClass = before.DogClass,
            CurrentClass = after.DogClass,
            XpRemaining = afterLevel.XpRemaining,
            ProgressPercent = afterLevel.ProgressPercent,
            ProgressionChanges = new DogProgressionChangesViewModel
            {
                Adventure = after.Adventure - before.Adventure,
                Social = after.Social - before.Social,
                Forest = after.Forest - before.Forest,
                City = after.City - before.City,
                Water = after.Water - before.Water,
                Speed = after.Speed - before.Speed
            }
        };

    public StreakRewardViewModel? BuildStreakReward(GamificationStreakInfo? streak, int previousDays, bool includeUnchanged = true)
    {
        if (streak == null || (!includeUnchanged && streak.StoredCurrentDays <= previousDays)) return null;
        return new StreakRewardViewModel
        {
            PreviousDays = previousDays,
            CurrentDays = streak.EffectiveCurrentDays,
            Increased = streak.StoredCurrentDays > previousDays,
            MilestoneReached = streak.StoredCurrentDays > previousDays && streak.StoredCurrentDays is 7 or 30 or 100 ? streak.StoredCurrentDays : null,
            State = streak.State
        };
    }

    public DogProgressionProfile Snapshot(DogProgressionProfile profile) => new()
    {
        DogId = profile.DogId,
        TotalXp = profile.TotalXp,
        Level = profile.Level,
        DogClass = profile.DogClass,
        Adventure = profile.Adventure,
        Social = profile.Social,
        Forest = profile.Forest,
        City = profile.City,
        Water = profile.Water,
        Speed = profile.Speed
    };
}
