namespace DoggyDrop.Services;

public sealed record UserAchievementDefinition(
    string Key,
    string DisplayName,
    string Description,
    string Category,
    string IconKey,
    double ProgressTarget,
    string ProgressUnit);

public static class UserAchievementCatalog
{
    public const string DogParent = "dog_parent";
    public const string WalkFirst = "walk_first";
    public const string Walk10Km = "walk_10km";
    public const string Walk100Km = "walk_100km";
    public const string BinFirstSubmission = "bin_first_submission";
    public const string Bin10Submissions = "bin_10_submissions";
    public const string Explorer5Places = "explorer_5_places";

    public static IReadOnlyList<UserAchievementDefinition> All { get; } =
    [
        new(DogParent, "Pasji skrbnik", "Dodaj prvega psa v profil.", "dogs", "dog", 1, "psov"),
        new(WalkFirst, "Prvi sprehod", "Zaključi prvi sprehod.", "walks", "walk", 1, "sprehodov"),
        new(Walk10Km, "Mestni pohodnik", "Skupaj prehodi 10 km.", "walks", "distance", 10, "km"),
        new(Walk100Km, "Mojster poti", "Skupaj prehodi 100 km.", "walks", "distance", 100, "km"),
        new(BinFirstSubmission, "Prvi predlog koša", "Oddaj prvi predlog pasjega koša.", "contributions", "bin", 1, "predlogov"),
        new(Bin10Submissions, "Junak predlogov", "Oddaj 10 predlogov pasjih košev.", "contributions", "bin", 10, "predlogov"),
        new(Explorer5Places, "Raziskovalec parkov", "Obišči 5 različnih pasjih parkov.", "exploration", "park", 5, "parkov")
    ];

    public static UserAchievementDefinition Get(string key) =>
        All.Single(definition => definition.Key == key);
}
