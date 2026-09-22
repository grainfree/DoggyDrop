namespace DoggyDrop.Services;

public sealed record AchievementReconciliationResult(
    int UsersDiscovered,
    int UsersProcessed,
    int UsersSucceeded,
    int UsersFailed,
    int AchievementsInserted,
    int AchievementsAlreadyOwned,
    TimeSpan Elapsed,
    bool SystemicFailure = false)
{
    public bool Succeeded =>
        !SystemicFailure && UsersFailed == 0 && UsersProcessed >= UsersDiscovered;
    public int ExitCode => Succeeded ? 0 : 1;
}
