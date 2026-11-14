using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UEManager.UI;

/// <summary>
/// The East Asian fallback in the theme's fonts, for the language shown. Palette.xaml names Microsoft YaHei UI;
/// Traditional Chinese gets Microsoft JhengHei UI, Japanese Yu Gothic UI, and Korean Malgun Gothic.
/// These fonts come with Windows and use the appropriate character shapes for each language.
/// </summary>
internal static class LanguageFonts
{
    private const string Simplified = "Microsoft YaHei UI";
    private const string Traditional = "Microsoft JhengHei UI";

    /// <summary>The fonts with an East Asian fallback; DynamicResource lets views follow a language switch.</summary>
    private static readonly string[] Keys = ["Font.Body", "Font.Heading", "Font.Label"];

    /// <summary>Sets the fonts for the language shown now, and again whenever it changes.</summary>
    public static void Follow(ResourceDictionary resources)
    {
        var fonts = Keys.ToDictionary(key => key, key => (FontFamily)resources[key]);
        void Apply()
        {
            foreach (var (key, font) in fonts)
                resources[key] = For(font, L.Current.Language);
        }
        Apply();
        L.Current.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Localizer.Language))
                Apply();
        };
    }

    /// <summary>Replaces YaHei with the Windows fallback for Traditional Chinese, Japanese or Korean.</summary>
    internal static FontFamily For(FontFamily font, string language)
    {
        var fallback = IsTraditionalChinese(language) ? Traditional
            : CultureInfo.GetCultureInfo(language).TwoLetterISOLanguageName switch
            {
                "ja" => "Yu Gothic UI",
                "ko" => "Malgun Gothic",
                _ => Simplified
            };
        return fallback != Simplified && font.Source.Contains(Simplified, StringComparison.OrdinalIgnoreCase)
            ? new FontFamily(font.BaseUri, font.Source.Replace(Simplified, fallback, StringComparison.OrdinalIgnoreCase))
            : font;
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
