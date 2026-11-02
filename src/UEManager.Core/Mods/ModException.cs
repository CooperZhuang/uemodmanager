namespace UEManager.Core.Mods;

/// <summary>
/// A problem the user can act on. <see cref="Exception.Message"/> is the English text, as the log keeps it;
/// the app shows <see cref="Text"/> in the player's language.
/// </summary>
public class ModException(FormattableString message) : Exception(FormattableString.Invariant(message))
{
    /// <summary>The message with its values kept apart, so the text around them can be translated.</summary>
    public FormattableString Text { get; } = message;
}

/// <summary>
/// Files of these mods were changed outside the manager, so it removed nothing. Keeping the player's version
/// (<see cref="ModManager.KeepChangedFiles"/>) is the way on.
/// </summary>
public sealed class ChangedFilesException(FormattableString message, IReadOnlyList<string> modIds) : ModException(message)
{
    public IReadOnlyList<string> ModIds { get; } = modIds;
}
