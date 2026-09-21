using DoggyDrop.Models;
using DoggyDrop.ViewModels;

namespace DoggyDrop.Services;

public interface IGamificationRewardBuilder
{
    UserRewardViewModel? BuildUserReward(UserXpEvent? xpEvent, GamificationLevelInfo before, GamificationLevelInfo after);
    DogRewardViewModel? BuildDogReward(Dog dog, DogXpEvent? xpEvent, DogProgressionLevelInfo beforeLevel, DogProgressionLevelInfo afterLevel, DogProgressionProfile before, DogProgressionProfile after);
    StreakRewardViewModel? BuildStreakReward(GamificationStreakInfo? streak, int previousDays, bool includeUnchanged = true);
    DogProgressionProfile Snapshot(DogProgressionProfile profile);
}
