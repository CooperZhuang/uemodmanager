using System.Text.Json.Serialization;

namespace UEManager.Core.Storage;

/// <summary>User choices that survive a restart.</summary>
public sealed class AppSettings
{
    /// <summary>The chosen language's code, such as "zh-CN"; null follows the Windows display language.</summary>
    public string? Language { get; set; }

    public const int DefaultUiScale = 100;
    public const int MinUiScale = 85;
    public const int MaxUiScale = 140;
    public const int UiScaleStep = 5;
    private const int LegacyDefaultFontSize = 13;

    /// <summary>The chosen interface font family, or "$system" for the Windows font; null uses the theme's fonts.</summary>
    public string? Font { get; set; }

    private int uiScale = DefaultUiScale;
    private bool hasUiScale;

    /// <summary>The interface scale in Settings (85% to 140% in steps of 5%; 100% is the default). Old settings without it keep 100%.</summary>
    public int UiScale
    {
        get => uiScale;
        set
        {
            hasUiScale = true;
            uiScale = NormalizeUiScale(value);
        }
    }

    /// <summary>Migrates a legacy fontSize setting (11–18 around a 13px baseline) when reading files without uiScale.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? FontSize
    {
        get => null;
        set
        {
            if (value is int legacy && !hasUiScale)
                uiScale = MigrateLegacyFontSize(legacy);
        }
    }

    public static int NormalizeUiScale(int percent)
    {
        if (percent <= 0)
            return DefaultUiScale;
        var snapped = (int)Math.Round(percent / (double)UiScaleStep, MidpointRounding.AwayFromZero) * UiScaleStep;
        return Math.Clamp(snapped, MinUiScale, MaxUiScale);
    }

    public static int MigrateLegacyFontSize(int size) =>
        size <= 0
            ? DefaultUiScale
            : NormalizeUiScale((int)Math.Round(size * 100.0 / LegacyDefaultFontSize, MidpointRounding.AwayFromZero));

    /// <summary>
    /// The game the app starts with, as a game profile's id; null until the player chose one, so the app asks. See
    /// <see cref="Games.GameChoice"/>.
    /// </summary>
    public string? Game { get; set; }

    /// <summary>The player chooses the game at every start; off: the app starts with <see cref="Game"/>.</summary>
    public bool AskForGameAtStart { get; set; }

    /// <summary>
    /// The developer's message in the game picker opened by itself once, at the very first start; from then on it stays
    /// closed until the player opens it.
    /// </summary>
    public bool DeveloperMessageShown { get; set; }

    /// <summary>Install folder per game profile id, set when the user locates the game by hand.</summary>
    public Dictionary<string, string> GamePaths { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The game's build (Steam build id or Epic version) per game profile id, as the player last confirmed it.</summary>
    public Dictionary<string, string> GameBuilds { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The save folder per game profile id, when the player chose it.</summary>
    public Dictionary<string, string> SaveFolders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The start arguments "Launch game" passes, per game profile id, when the player changed them; an empty text means
    /// none. A game without an entry starts with its profile's recommended arguments. See <see cref="Games.LaunchOptions"/>.
    /// </summary>
    public Dictionary<string, string> LaunchArguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>"Launch game" zips the save folder first. Off until the player turns it on.</summary>
    public bool BackUpSavesOnLaunch { get; set; }

    private int saveBackupKeep = Games.SaveBackups.DefaultKeep;

    /// <summary>
    /// How many save backups to keep per game, as the Settings slider has it (see <see cref="Games.SaveBackups.Normalize"/>).
    /// Old settings without it keep the default.
    /// </summary>
    public int SaveBackupKeep
    {
        get => saveBackupKeep;
        set => saveBackupKeep = Games.SaveBackups.Normalize(value);
    }

    /// <summary>At each start, the app asks the mod site for the page version of the mods with a mod page. On until the player turns it off; the Mods page's button works either way.</summary>
    public bool CheckModUpdatesAtStart { get; set; } = true;

    /// <summary>The newest app version the banner at start announced; each new version is announced once.</summary>
    public string? AnnouncedUpdate { get; set; }

    /// <summary>The Mods page's sort column, such as "Name" or "Installed"; null for the default.</summary>
    public string? ModsSort { get; set; }

    public bool ModsSortDescending { get; set; }

    /// <summary>The mods the Mods page shows by their state, "Enabled" or "Disabled"; null for all of them.</summary>
    public string? ModsState { get; set; }

    /// <summary>The openmods page's sort field, "Name" or "Updated"; null for the default, the newest release first.</summary>
    public string? CatalogSort { get; set; }

    public bool CatalogSortDescending { get; set; }

    /// <summary>The openmods page hides the mods and tools the player installed from it. Off until the player turns it on.</summary>
    public bool CatalogHideInstalled { get; set; }

    /// <summary>The player removed the Donate button from the sidebar. The About page keeps it.</summary>
    public bool HideDonateButton { get; set; }

    /// <summary>The icon next to the clock, with its menu to switch the game. On until the player turns it off.</summary>
    public bool ShowTrayIcon { get; set; } = true;

    /// <summary>Minimize hides the window in the tray. Off until the player turns it on; only with the tray icon.</summary>
    public bool MinimizeToTray { get; set; }

    /// <summary>The X hides the window in the tray instead of closing the app; only with both settings above on.</summary>
    public bool CloseToTray { get; set; }

    /// <summary>
    /// The app has no taskbar button, only the tray icon; minimize then always hides the window in the tray. Off until
    /// the player turns it on; only with the tray icon, so the window can always come back.
    /// </summary>
    public bool TrayIconOnly { get; set; }

    /// <summary>Where the main window was when it last closed; null opens it centred at its default size.</summary>
    public WindowPlacement? Window { get; set; }

    public static AppSettings Load(AppPaths paths)
    {
        try
        {
            return JsonFile.Read<AppSettings>(paths.SettingsFile) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            Log.Warn($"Settings could not be read, defaults are used: {ex.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppPaths paths) => JsonFile.Write(paths.SettingsFile, this);
}

/// <summary>A window's place and size in device-independent pixels; when maximized, the size it returns to.</summary>
public sealed record WindowPlacement(double Left, double Top, double Width, double Height, bool Maximized);
