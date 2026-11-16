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

    /// <summary>Titles keep the theme's font, the game's look; the chosen font serves the scripts it lacks.</summary>
    [Theory]
    [InlineData("zh-CN", "Palatino Linotype, Georgia, Consolas, Microsoft YaHei UI")]
    [InlineData("zh-TW", "Palatino Linotype, Georgia, Consolas, Microsoft JhengHei UI")]
    [InlineData("ko", "Palatino Linotype, Georgia, Consolas, Malgun Gothic")]
    public void A_title_keeps_the_theme_font_before_a_custom_font(string language, string expected)
    {
        var heading = new FontFamily("Palatino Linotype, Georgia, Microsoft YaHei UI");
        Assert.Equal(expected, LanguageFonts.For(heading, language, "Consolas", keepTheme: true).Source);
        // Without a choice it is the language's font only, as before.
        Assert.Equal("Palatino Linotype, Georgia, Microsoft JhengHei UI", LanguageFonts.For(heading, "zh-TW", null, keepTheme: true).Source);
    }

    /// <summary>A start lists only the fixed choices; every installed font waits for the list to open.</summary>
    [Fact]
    public void Before_the_list_opens_only_the_fixed_choices_are_listed()
    {
        Assert.Equal([LanguageFonts.Default, LanguageFonts.System], LanguageFonts.FirstOptions(null).Select(option => option.Code));
        Assert.Equal([LanguageFonts.Default, LanguageFonts.System], LanguageFonts.FirstOptions(LanguageFonts.System).Select(option => option.Code));
        // A chosen font must show as chosen: the whole list, which the start looked up for it anyway.
        Assert.Equal(LanguageFonts.Options().Count, LanguageFonts.FirstOptions("Consolas").Count);
    }

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

            // Titles keep the theme's font first (the game's look), the chosen font for other scripts.
            Assert.Equal("Palatino Linotype, Georgia, Consolas, Microsoft JhengHei UI", Assert.IsType<FontFamily>(resources["Font.Heading"]).Source);
            // Tooltips follow the chosen font.
            Assert.Same(resources["Font.Body"], resources["Font.Tip"]);

            // Calling Follow again while overridden must keep the original theme base font and never double-apply.
            LanguageFonts.Follow(resources);
            LanguageFonts.SetFont(null);
            Localizer.Current.SetLanguage("en");

            Assert.Equal("Segoe UI, Microsoft YaHei UI", Assert.IsType<FontFamily>(resources["Font.Body"]).Source);
            Assert.Equal("Palatino Linotype, Georgia, Microsoft YaHei UI", Assert.IsType<FontFamily>(resources["Font.Heading"]).Source);
            Assert.Equal("Bahnschrift, Segoe UI, Microsoft YaHei UI", Assert.IsType<FontFamily>(resources["Font.Label"]).Source);
            // With no chosen font, tooltips get no font from the app, as before the font choice existed.
            Assert.False(resources.Contains("Font.Tip"));
        }
        finally
        {
            Localizer.Current.SetLanguage(originalLanguage);
            LanguageFonts.SetFont(originalFont);
            LanguageFonts.SetUiScale(originalScale);
        }
    }

    /// <summary>The tooltip style takes Font.Tip, which is set only under a chosen font. Without it, the setter must act
    /// as if it were not there, so a tooltip keeps the font it had before the font choice existed. (A ToolTip itself
    /// cannot sit in a tree outside a shown popup, so a Label with the same setter stands in.)</summary>
    [Fact]
    public void A_tooltip_without_a_chosen_font_keeps_its_old_font()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                // The same setter as the tooltip style, on a control that sits in the tree and inherits its font.
                var style = new Style(typeof(System.Windows.Controls.Label));
                style.Setters.Add(new Setter(System.Windows.Controls.Control.FontFamilyProperty, new DynamicResourceExtension("Font.Tip")));
                var host = new System.Windows.Controls.StackPanel();
                System.Windows.Documents.TextElement.SetFontFamily(host, new FontFamily("Consolas"));
                var before = new System.Windows.Controls.Label();
                var styled = new System.Windows.Controls.Label { Style = style };
                host.Children.Add(before);
                host.Children.Add(styled);

                // No Font.Tip: the font the control had without the setter (here the inherited one).
                Assert.Equal("Consolas", before.FontFamily.Source);
                Assert.Same(before.FontFamily, styled.FontFamily);

                var chosen = new FontFamily("Georgia");
                host.Resources["Font.Tip"] = chosen;
                Assert.Same(chosen, styled.FontFamily);

                host.Resources.Remove("Font.Tip");
                Assert.Same(before.FontFamily, styled.FontFamily);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), "The tooltip font check did not finish.");
        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
