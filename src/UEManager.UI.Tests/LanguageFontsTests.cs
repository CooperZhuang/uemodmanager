using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace UEManager.UI.Tests;

[Collection("Language")]
public class LanguageFontsTests
{
    [Theory]
    [InlineData("zh-TW", "Segoe UI, Microsoft JhengHei UI")]
    [InlineData("zh-HK", "Segoe UI, Microsoft JhengHei UI")]
    [InlineData("zh-CN", "Segoe UI, Microsoft YaHei UI")]
    [InlineData("vi", "Segoe UI, Microsoft YaHei UI")]
    [InlineData("en", "Segoe UI, Microsoft YaHei UI")]
    [InlineData("ja", "Segoe UI, Yu Gothic UI")]
    [InlineData("ja-JP", "Segoe UI, Yu Gothic UI")]
    [InlineData("ko", "Segoe UI, Malgun Gothic")]
    [InlineData("ko-KR", "Segoe UI, Malgun Gothic")]
    public void Each_language_gets_its_font(string language, string expected) =>
        Assert.Equal(expected, LanguageFonts.For(new FontFamily("Segoe UI, Microsoft YaHei UI"), language).Source);

    [Fact]
    public void A_font_without_a_Chinese_font_stays_as_it_is()
    {
        var icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        Assert.Same(icons, LanguageFonts.For(icons, "zh-TW"));
        Assert.Same(icons, LanguageFonts.For(icons, "zh-TW", "Consolas"));
    }

    [Theory]
    [InlineData("zh-CN", "Consolas, Microsoft YaHei UI")]
    [InlineData("zh-TW", "Consolas, Microsoft JhengHei UI")]
    [InlineData("ja", "Consolas, Yu Gothic UI")]
    [InlineData("ko", "Consolas, Malgun Gothic")]
    public void A_custom_font_leads_the_language_fallback(string language, string expected) =>
        Assert.Equal(expected, LanguageFonts.For(new FontFamily("Segoe UI, Microsoft YaHei UI"), language, "Consolas").Source);

    [Fact]
    public void Default_choice_uses_the_theme_font_and_system_choice_uses_the_windows_font()
    {
        var theme = new FontFamily("Palatino Linotype, Georgia, Microsoft YaHei UI");
        Assert.Equal("Palatino Linotype, Georgia, Microsoft JhengHei UI",
            LanguageFonts.For(theme, "zh-TW", LanguageFonts.Default).Source);

        var systemFont = SystemFonts.MessageFontFamily.Source;
        var expected = systemFont.Contains("Microsoft YaHei UI", StringComparison.OrdinalIgnoreCase)
            ? systemFont
            : $"{systemFont}, Microsoft YaHei UI";
        Assert.Equal(expected, LanguageFonts.For(theme, "zh-CN", LanguageFonts.System).Source);
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(50, 0.85)]
    [InlineData(85, 0.85)]
    [InlineData(100, 1.0)]
    [InlineData(123, 1.25)]
    [InlineData(140, 1.40)]
    [InlineData(200, 1.40)]
    public void Font_scale_matches_the_normalized_ui_scale(int percent, double expected)
    {
        var scale = LanguageFonts.ScaleFor(percent);
        Assert.Equal(expected, scale.ScaleX, precision: 6);
        Assert.Equal(expected, scale.ScaleY, precision: 6);
    }

    [Fact]
    public void Follow_updates_scale_without_replacing_fonts_when_only_ui_scale_changes()
    {
        var originalFont = LanguageFonts.Font;
        var originalScale = LanguageFonts.UiScale;
        try
        {
            LanguageFonts.SetFont(null);
            LanguageFonts.SetUiScale(100);
            var resources = new ResourceDictionary
            {
                ["Font.Body"] = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                ["Font.Heading"] = new FontFamily("Palatino Linotype, Georgia, Microsoft YaHei UI"),
                ["Font.Label"] = new FontFamily("Bahnschrift, Segoe UI, Microsoft YaHei UI"),
                ["Font.Scale"] = new ScaleTransform(1, 1),
            };
            LanguageFonts.Follow(resources);

            var body = resources["Font.Body"];
            var heading = resources["Font.Heading"];
            var label = resources["Font.Label"];

            LanguageFonts.SetUiScale(125);

            Assert.Same(body, resources["Font.Body"]);
            Assert.Same(heading, resources["Font.Heading"]);
            Assert.Same(label, resources["Font.Label"]);
            var scale = Assert.IsType<ScaleTransform>(resources["Font.Scale"]);
            Assert.Equal(1.25, scale.ScaleX, precision: 6);
            Assert.Equal(1.25, scale.ScaleY, precision: 6);
        }
        finally
        {
            LanguageFonts.SetFont(originalFont);
            LanguageFonts.SetUiScale(originalScale);
        }
    }

    [Fact]
    public void Symbol_and_virtual_fonts_are_excluded_from_options_and_resolve_to_default()
    {
        var originalFont = LanguageFonts.Font;
        try
        {
            var options = LanguageFonts.Options();
            Assert.True(options.Count > 2);
            Assert.Equal(LanguageFonts.Default, options[0].Code);
            Assert.Equal(LanguageFonts.System, options[1].Code);

            var excluded = new[]
            {
                "Marlett",
                "Segoe Fluent Icons",
                "Segoe MDL2 Assets",
                "Symbol",
                "Webdings",
                "Wingdings",
            };
            foreach (var option in options.Skip(2))
            {
                Assert.False(string.IsNullOrWhiteSpace(option.Code));
                Assert.False(string.IsNullOrWhiteSpace(option.Name));
                Assert.NotNull(option.PreviewFont);
                Assert.DoesNotContain(option.Code, excluded, StringComparer.OrdinalIgnoreCase);
                Assert.False(option.Code.StartsWith("Global ", StringComparison.OrdinalIgnoreCase));
            }

            foreach (var invalid in excluded.Concat(["Global User Interface", "NoSuchFont_12345"]))
            {
                Assert.Equal(LanguageFonts.Default, LanguageFonts.Resolve(invalid));
                LanguageFonts.SetFont(invalid);
                Assert.Null(LanguageFonts.Font);
            }

            LanguageFonts.SetFont(LanguageFonts.System);
            Assert.Equal(LanguageFonts.System, LanguageFonts.Font);
            LanguageFonts.SetFont("Consolas");
            Assert.Equal("Consolas", LanguageFonts.Font);
            LanguageFonts.SetFont(LanguageFonts.Default);
            Assert.Null(LanguageFonts.Font);
        }
        finally
        {
            LanguageFonts.SetFont(originalFont);
        }
    }

    [Fact]
    public void Options_and_SortOptions_pin_fixed_entries_sort_localized_names_and_keep_instances()
    {
        var originalLanguage = Localizer.Current.Language;
        try
        {
            Localizer.Current.SetLanguage("en");
            var observable = new ObservableCollection<FontOption>(LanguageFonts.Options());
            var consolas = observable.First(o => o.Code.Equals("Consolas", StringComparison.OrdinalIgnoreCase));

            Localizer.Current.SetLanguage("zh-CN");
            LanguageFonts.SortOptions(observable);

            Assert.Equal(LanguageFonts.Default, observable[0].Code);
            Assert.Equal(LanguageFonts.System, observable[1].Code);
            Assert.Same(consolas, observable.First(o => o.Code.Equals("Consolas", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(
                LanguageFonts.Options().Skip(2).Select(o => o.Code),
                observable.Skip(2).Select(o => o.Code));
        }
        finally
        {
            Localizer.Current.SetLanguage(originalLanguage);
        }
    }

    [Fact]
    public void Fallback_check_matches_exact_family_segments_and_PreviewFor_follows_language()
    {
        var originalLanguage = Localizer.Current.Language;
        try
        {
            var theme = new FontFamily("Segoe UI, Microsoft YaHei UI");
            Assert.Equal(
                "Yu Gothic UI Semibold, Yu Gothic UI",
                LanguageFonts.For(theme, "ja", "Yu Gothic UI Semibold").Source);
            Assert.Equal(
                "Yu Gothic UI",
                LanguageFonts.For(theme, "ja", "Yu Gothic UI").Source);

            Localizer.Current.SetLanguage("ja");
            Assert.Equal("Consolas, Yu Gothic UI", LanguageFonts.PreviewFor("Consolas").Source);
            Assert.Equal("Segoe UI Variable Text, Segoe UI, Yu Gothic UI", LanguageFonts.PreviewFor(LanguageFonts.Default).Source);

            Localizer.Current.SetLanguage("ko");
            Assert.Equal("Consolas, Malgun Gothic", LanguageFonts.PreviewFor("Consolas").Source);
        }
        finally
        {
            Localizer.Current.SetLanguage(originalLanguage);
        }
    }

    [Fact]
    public void Follow_is_idempotent_and_preserves_original_base_fonts()
    {
        var originalLanguage = Localizer.Current.Language;
        var originalFont = LanguageFonts.Font;
        var originalScale = LanguageFonts.UiScale;
        try
        {
            Localizer.Current.SetLanguage("en");
            LanguageFonts.SetFont(null);
            LanguageFonts.SetUiScale(100);

            var resources = new ResourceDictionary
            {
                ["Font.Body"] = new FontFamily("Segoe UI, Microsoft YaHei UI"),
                ["Font.Heading"] = new FontFamily("Palatino Linotype, Georgia, Microsoft YaHei UI"),
                ["Font.Label"] = new FontFamily("Bahnschrift, Segoe UI, Microsoft YaHei UI"),
                ["Font.Scale"] = new ScaleTransform(1, 1),
            };

            LanguageFonts.Follow(resources);
            LanguageFonts.SetFont("Consolas");
            Localizer.Current.SetLanguage("zh-TW");
            LanguageFonts.SetUiScale(125);

            Assert.Equal("Consolas, Microsoft JhengHei UI", Assert.IsType<FontFamily>(resources["Font.Heading"]).Source);

            // Calling Follow again while overridden must keep the original theme base font and never double-apply.
            LanguageFonts.Follow(resources);
            LanguageFonts.SetFont(null);
            Localizer.Current.SetLanguage("en");

            Assert.Equal("Segoe UI, Microsoft YaHei UI", Assert.IsType<FontFamily>(resources["Font.Body"]).Source);
            Assert.Equal("Palatino Linotype, Georgia, Microsoft YaHei UI", Assert.IsType<FontFamily>(resources["Font.Heading"]).Source);
            Assert.Equal("Bahnschrift, Segoe UI, Microsoft YaHei UI", Assert.IsType<FontFamily>(resources["Font.Label"]).Source);
        }
        finally
        {
            Localizer.Current.SetLanguage(originalLanguage);
            LanguageFonts.SetFont(originalFont);
            LanguageFonts.SetUiScale(originalScale);
        }
    }
}
