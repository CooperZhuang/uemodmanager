using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using UEManager.Core.Storage;

namespace UEManager.UI;

/// <summary>A font shown in Settings: its saved name (empty for the theme default, "$system" for the Windows font) and its display name.</summary>
public sealed class FontOption : INotifyPropertyChanged
{
    private readonly Func<string> name;
    private readonly Func<FontFamily> previewFont;

    public FontOption(string code, string name) : this(code, () => name, () => LanguageFonts.PreviewFor(code))
    {
    }

    internal FontOption(string code, Func<string> name, Func<FontFamily> previewFont)
    {
        Code = code;
        this.name = name;
        this.previewFont = previewFont;
    }

    public string Code { get; }

    public string Name => name();

    public FontFamily PreviewFont => previewFont();

    public event PropertyChangedEventHandler? PropertyChanged;

    internal void Refresh()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PreviewFont)));
    }

    public override string ToString() => Name;
}

/// <summary>
/// The East Asian fallback and user font choices for the interface. Palette.xaml names Microsoft YaHei UI;
/// Traditional Chinese gets Microsoft JhengHei UI, Japanese Yu Gothic UI, and Korean Malgun Gothic.
/// A chosen font (or the Windows font) leads the body text and labels while keeping the language's fallback; titles
/// keep the theme's font for their letters and take the chosen font for other scripts. Font.Scale scales the UI
/// proportionally from its 100% baseline.
/// </summary>
internal static class LanguageFonts
{
    public const string Default = "";
    public const string System = "$system";

    private const string Simplified = "Microsoft YaHei UI";
    private const string Traditional = "Microsoft JhengHei UI";

    private static readonly FontFamily DefaultPreviewBase = new("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI");

    /// <summary>The fonts with an East Asian fallback; DynamicResource lets views follow a language or font switch.</summary>
    private static readonly string[] Keys = ["Font.Body", "Font.Heading", "Font.Label"];

    /// <summary>The titles' font: it keeps the theme's fonts first under a chosen font (see <see cref="For"/>).</summary>
    private const string HeadingKey = "Font.Heading";

    /// <summary>The tooltips' font: the body font under a chosen font; with no choice the key is absent, so tooltips
    /// keep the font they had before the font choice existed.</summary>
    private const string TipKey = "Font.Tip";

    private static readonly Lazy<IReadOnlyList<FontFamily>> SystemFontFamilies = new(() =>
        Fonts.SystemFontFamilies
            .Where(f => !string.IsNullOrWhiteSpace(f.Source) && IsTextFont(f))
            .DistinctBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .OrderBy(f => f.Source, StringComparer.OrdinalIgnoreCase)
            .ToList());

    private sealed class FollowTarget(IReadOnlyDictionary<string, FontFamily> baseFonts, Dispatcher? dispatcher)
    {
        public IReadOnlyDictionary<string, FontFamily> BaseFonts { get; } = baseFonts;
        public Dispatcher? Dispatcher { get; } = dispatcher;
    }

    private static readonly ConditionalWeakTable<ResourceDictionary, FollowTarget> Targets = new();
    private static readonly List<WeakReference<ResourceDictionary>> Tracked = [];
    private static readonly object TrackedLock = new();

    static LanguageFonts() =>
        L.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Localizer.Language))
                ApplyTracked(fonts: true, scale: false);
        };

    public static string? Font { get; private set; }

    public static int UiScale { get; private set; } = AppSettings.DefaultUiScale;

    public static void SetFont(string? font)
    {
        var resolved = Resolve(font);
        var normalized = resolved == Default ? null : resolved;
        if (string.Equals(Font, normalized, StringComparison.Ordinal))
            return;
        Font = normalized;
        ApplyTracked(fonts: true, scale: false);
    }

    public static void SetUiScale(int percent)
    {
        var normalized = AppSettings.NormalizeUiScale(percent);
        if (UiScale == normalized)
            return;
        UiScale = normalized;
        ApplyTracked(fonts: false, scale: true);
    }

    /// <summary>Sets the fonts and scale for the language and user choices now, and again whenever any of them changes.</summary>
    public static void Follow(ResourceDictionary resources)
    {
        FollowTarget target;
        lock (TrackedLock)
        {
            if (!Targets.TryGetValue(resources, out target!))
            {
                var baseFonts = Keys.ToDictionary(key => key, key => (FontFamily)resources[key]);
                target = new FollowTarget(baseFonts, Dispatcher.FromThread(Thread.CurrentThread));
                Targets.Add(resources, target);
                PruneDeadTargetsLocked();
                Tracked.Add(new WeakReference<ResourceDictionary>(resources));
            }
        }
        ApplyTo(resources, target, fonts: true, scale: true);
    }

    /// <summary>Replaces YaHei with the Windows fallback for Traditional Chinese, Japanese or Korean, or puts a chosen font first.</summary>
    /// <param name="keepTheme">
    /// A heading: the theme's own fonts stay first, as they make each game's look, and the chosen font comes before the
    /// language's fallback, for the scripts the theme's fonts lack, such as Chinese (2026-11-16).
    /// </param>
    internal static FontFamily For(FontFamily font, string language, string? choice = null, bool keepTheme = false)
    {
        if (!font.Source.Contains(Simplified, StringComparison.OrdinalIgnoreCase))
            return font;
        var fallback = FallbackFor(language);
        if (string.IsNullOrWhiteSpace(choice))
        {
            return fallback != Simplified
                ? new FontFamily(font.BaseUri, font.Source.Replace(Simplified, fallback, StringComparison.OrdinalIgnoreCase))
                : font;
        }
        var chosen = choice.Equals(System, StringComparison.OrdinalIgnoreCase)
            ? SystemFonts.MessageFontFamily.Source
            : choice.Trim();
        var lead = ContainsFamily(chosen, fallback) ? chosen : $"{chosen}, {fallback}";
        if (!keepTheme)
            return new FontFamily(lead);
        var theme = string.Join(", ", font.Source.Split(',').Select(part => part.Trim())
            .Where(part => part.Length > 0 && !part.Equals(Simplified, StringComparison.OrdinalIgnoreCase)));
        return new FontFamily(font.BaseUri, theme.Length == 0 ? lead : $"{theme}, {lead}");
    }

    internal static ScaleTransform ScaleFor(int percent)
    {
        var scale = AppSettings.NormalizeUiScale(percent) / 100.0;
        var transform = new ScaleTransform(scale, scale);
        transform.Freeze();
        return transform;
    }

    internal static FontFamily PreviewFor(string code) =>
        For(DefaultPreviewBase, L.Current.Language, string.IsNullOrEmpty(code) ? null : code);

    /// <summary>
    /// The font choices before Settings' list opens: the theme's fonts and the Windows font, and the chosen font if it is
    /// another one. Every installed font comes with <see cref="Options"/>, only when needed: looking through them all took
    /// 0.15 s on a PC with 83 fonts and can take a second with hundreds (2026-11-16).
    /// </summary>
    public static IReadOnlyList<FontOption> FirstOptions(string? chosen) =>
        string.IsNullOrWhiteSpace(chosen) || chosen.Equals(System, StringComparison.OrdinalIgnoreCase)
            ? Pinned()
            // A chosen font was looked up at the start (Resolve), so the list is known already.
            : Options();

    private static FontOption[] Pinned() =>
    [
        new FontOption(Default, () => L.T("Theme default"), () => PreviewFor(Default)),
        new FontOption(System, () => L.T("System font"), () => PreviewFor(System)),
    ];

    public static IReadOnlyList<FontOption> Options()
    {
        var pinned = Pinned();
        var comparer = NameComparer(L.Current.Language);
        var fonts = SystemFontFamilies.Value
            .Select(f => new FontOption(
                f.Source,
                () => DisplayName(f, CultureInfo.GetCultureInfo(L.Current.Language)),
                () => PreviewFor(f.Source)))
            .OrderBy(option => option.Name, comparer)
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase);
        return [.. pinned, .. fonts];
    }

    /// <summary>Re-sorts the system font options by their display name in the current language while keeping the first two entries pinned.</summary>
    internal static void SortOptions(IList<FontOption> options)
    {
        if (options.Count <= 3)
            return;
        var comparer = NameComparer(L.Current.Language);
        var sorted = options.Skip(2)
            .OrderBy(option => option.Name, comparer)
            .ThenBy(option => option.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (options is ObservableCollection<FontOption> observable)
        {
            for (var target = 0; target < sorted.Count; target++)
            {
                var current = IndexOfFrom(observable, sorted[target], target + 2);
                if (current != target + 2)
                    observable.Move(current, target + 2);
            }
            return;
        }
        for (var i = 0; i < sorted.Count; i++)
            options[i + 2] = sorted[i];
    }

    public static string Resolve(string? choice)
    {
        if (string.IsNullOrWhiteSpace(choice))
            return Default;
        var trimmed = choice.Trim();
        if (trimmed.Equals(System, StringComparison.OrdinalIgnoreCase))
            return System;
        return SystemFontFamilies.Value.FirstOrDefault(f => f.Source.Equals(trimmed, StringComparison.OrdinalIgnoreCase))?.Source ?? Default;
    }

    private static void ApplyTracked(bool fonts, bool scale)
    {
        List<(ResourceDictionary Resources, FollowTarget Target)> live;
        lock (TrackedLock)
        {
            PruneDeadTargetsLocked();
            live = new List<(ResourceDictionary, FollowTarget)>(Tracked.Count);
            foreach (var weak in Tracked)
            {
                if (weak.TryGetTarget(out var resources) && Targets.TryGetValue(resources, out var target))
                    live.Add((resources, target));
            }
        }
        foreach (var (resources, target) in live)
        {
            if (target.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
            {
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    dispatcher.BeginInvoke(() => ApplyTo(resources, target, fonts, scale));
            }
            else
            {
                ApplyTo(resources, target, fonts, scale);
            }
        }
    }

    private static void PruneDeadTargetsLocked()
    {
        for (var i = Tracked.Count - 1; i >= 0; i--)
        {
            if (!Tracked[i].TryGetTarget(out var resources)
                || !Targets.TryGetValue(resources, out var target)
                || target.Dispatcher is { HasShutdownStarted: true } or { HasShutdownFinished: true })
                Tracked.RemoveAt(i);
        }
    }

    private static void ApplyTo(ResourceDictionary resources, FollowTarget target, bool fonts, bool scale)
    {
        if (fonts)
        {
            foreach (var (key, font) in target.BaseFonts)
                resources[key] = For(font, L.Current.Language, Font, keepTheme: key == HeadingKey);
            if (Font is null)
            {
                if (resources.Contains(TipKey))
                    resources.Remove(TipKey);
            }
            else
                resources[TipKey] = resources["Font.Body"];
        }
        if (scale)
            resources["Font.Scale"] = ScaleFor(UiScale);
    }

    private static int IndexOfFrom(ObservableCollection<FontOption> list, FontOption item, int start)
    {
        for (var i = start; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
                return i;
        }
        return -1;
    }

    /// <summary>Excludes symbol/icon fonts (e.g. Wingdings, Segoe MDL2 Assets) and WPF's synthetic composite placeholders.</summary>
    private static bool IsTextFont(FontFamily family)
    {
        foreach (var typeface in family.GetTypefaces())
        {
            if (typeface.TryGetGlyphTypeface(out var glyph))
                return !glyph.Symbol;
        }
        return false;
    }

    private static bool ContainsFamily(string source, string family) =>
        source.Split(',').Any(part => part.Trim().Equals(family, StringComparison.OrdinalIgnoreCase));

    private static StringComparer NameComparer(string language)
    {
        try
        {
            return StringComparer.Create(CultureInfo.GetCultureInfo(language), ignoreCase: true);
        }
        catch (CultureNotFoundException)
        {
            return StringComparer.OrdinalIgnoreCase;
        }
    }

    private static string FallbackFor(string language) =>
        IsTraditionalChinese(language) ? Traditional
            : CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName switch
            {
                "ja" => "Yu Gothic UI",
                "ko" => "Malgun Gothic",
                _ => Simplified,
            };

    private static string DisplayName(FontFamily family, CultureInfo culture)
    {
        var names = family.FamilyNames;
        if (TryName(names, culture.IetfLanguageTag) is { } exact)
            return exact;
        try
        {
            var specific = CultureInfo.CreateSpecificCulture(culture.Name).IetfLanguageTag;
            if (TryName(names, specific) is { } regional)
                return regional;
        }
        catch (CultureNotFoundException)
        {
        }
        var prefix = culture.TwoLetterISOLanguageName + "-";
        foreach (var entry in names)
        {
            if (entry.Key.IetfLanguageTag.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }
        return TryName(names, "en-us") ?? family.Source;
    }

    private static string? TryName(LanguageSpecificStringDictionary names, string tag)
    {
        if (string.IsNullOrEmpty(tag))
            return null;
        try
        {
            return names.TryGetValue(XmlLanguage.GetLanguage(tag), out var name) ? name : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>zh-TW, and any other language written in Traditional characters, such as zh-HK.</summary>
    private static bool IsTraditionalChinese(string language)
    {
        for (var culture = CultureInfo.GetCultureInfo(language); culture.Name.Length > 0; culture = culture.Parent)
        {
            if (culture.Name.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
