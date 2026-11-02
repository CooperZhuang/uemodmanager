using System.Windows.Media;
using Xunit;

namespace UEManager.UI.Tests;

public class LanguageFontsTests
{
    [Theory]
    [InlineData("zh-TW", "Segoe UI, Microsoft JhengHei UI")]
    [InlineData("zh-HK", "Segoe UI, Microsoft JhengHei UI")]
    [InlineData("zh-CN", "Segoe UI, Microsoft YaHei UI")]
    [InlineData("vi", "Segoe UI, Microsoft YaHei UI")]
    [InlineData("en", "Segoe UI, Microsoft YaHei UI")]
    public void Traditional_Chinese_gets_the_Taiwan_font(string language, string expected) =>
        Assert.Equal(expected, LanguageFonts.For(new FontFamily("Segoe UI, Microsoft YaHei UI"), language).Source);

    [Fact]
    public void A_font_without_a_Chinese_font_stays_as_it_is()
    {
        var icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        Assert.Same(icons, LanguageFonts.For(icons, "zh-TW"));
    }
}
