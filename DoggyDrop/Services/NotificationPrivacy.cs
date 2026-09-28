namespace DoggyDrop.Services;

// The legacy schema identifies only the recipient, not the actor or private walk.
// Never try to recover attribution by matching names/emails in arbitrary text.
public static class NotificationPrivacy
{
    public const string ObsoleteWalkStart = "FriendStartedWalk";
    public static IReadOnlyList<string> ActorMessageTypes { get; } =
        Array.AsReadOnly(new[] { ObsoleteWalkStart, "WalkReaction", "WalkComment", "PlaydateInvite", "PlaydateInterest" });

    public static string? NeutralTitle(string type) => type switch
    {
        ObsoleteWalkStart => "Obvestilo ni več na voljo",
        "WalkReaction" => "Odziv na sprehod",
        "WalkComment" => "Komentar na sprehod",
        "PlaydateInvite" => "Povabilo na sprehod",
        "PlaydateInterest" => "Nov odziv na povabilo",
        _ => null
    };

    public static string? NeutralBody(string type) => type switch
    {
        ObsoleteWalkStart => "Obvestilo o aktivnosti ni več na voljo.",
        "WalkReaction" => "Na tvojem sprehodu je nova reakcija.",
        "WalkComment" => "Na tvojem sprehodu je nov komentar.",
        "PlaydateInvite" => "Prejel/a si povabilo na sprehod. Podrobnosti najdeš med povabili.",
        "PlaydateInterest" => "Na tvoje povabilo na sprehod je prispel nov odziv.",
        _ => null
    };

    public static string Title(string type, string storedTitle) => NeutralTitle(type) ?? storedTitle;
    public static string Body(string type, string storedBody) => NeutralBody(type) ?? storedBody;
    public static string? Link(string type, string? storedLink) => type switch
    {
        ObsoleteWalkStart or "WalkReaction" or "WalkComment" => null,
        "PlaydateInvite" or "PlaydateInterest" => "/Playdates",
        _ => storedLink
    };
}
