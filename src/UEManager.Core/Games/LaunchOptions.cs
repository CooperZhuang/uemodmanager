using UEManager.Core.Storage;

namespace UEManager.Core.Games;

/// <summary>
/// The start arguments "Launch game" passes to a game: the player's own when they changed them, else the ones the game's
/// profile recommends (<see cref="GameProfile.LaunchArguments"/>, such as Black Myth: Wukong's -fileopenlog).
/// </summary>
public static class LaunchOptions
{
    /// <summary>The arguments the game starts with; null for none.</summary>
    public static string? For(AppSettings settings, GameProfile profile) =>
        settings.LaunchArguments.TryGetValue(profile.Id, out var chosen) ? Clean(chosen) : Recommended(profile);

    /// <summary>The arguments the game's profile recommends; null for none.</summary>
    public static string? Recommended(GameProfile profile) => Clean(profile.LaunchArguments);

    /// <summary>
    /// Keeps the player's arguments for the game; an empty text means none. The recommended ones are not kept, so a later
    /// change of the recommendation reaches the player.
    /// </summary>
    public static void Set(AppSettings settings, GameProfile profile, string? arguments)
    {
        var text = Clean(arguments);
        if (text == Recommended(profile))
            settings.LaunchArguments.Remove(profile.Id);
        else
            settings.LaunchArguments[profile.Id] = text ?? "";
    }

    /// <summary>The arguments without spaces around them; null when there are none.</summary>
    public static string? Clean(string? arguments) => string.IsNullOrWhiteSpace(arguments) ? null : arguments.Trim();
}
