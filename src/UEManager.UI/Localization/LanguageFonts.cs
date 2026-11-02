using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace UEManager.UI;

/// <summary>
/// The Chinese font in the theme's fonts, for the language shown. Palette.xaml names Microsoft YaHei UI, which draws
/// the characters as mainland China writes them; Traditional Chinese gets Microsoft JhengHei UI, which draws them as
/// Taiwan and Hong Kong write them. Both come with Windows.
/// </summary>
internal static class LanguageFonts
{
    private const string Simplified = "Microsoft YaHei UI";
    private const string Traditional = "Microsoft JhengHei UI";

    /// <summary>The fonts with a Chinese font in them; views take them as DynamicResource, so they follow a switch.</summary>
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

    /// <summary>A font for a language: JhengHei in place of YaHei for Traditional Chinese, else the font as it is.</summary>
    internal static FontFamily For(FontFamily font, string language) =>
        IsTraditionalChinese(language) && font.Source.Contains(Simplified, StringComparison.OrdinalIgnoreCase)
            ? new FontFamily(font.BaseUri, font.Source.Replace(Simplified, Traditional, StringComparison.OrdinalIgnoreCase))
            : font;

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
