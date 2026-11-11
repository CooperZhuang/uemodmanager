using UEManager.Core.Storage;

namespace UEManager.Core.Tests;

public class AppSettingsTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"saveBackupKeep\":0}")]
    [InlineData("{\"saveBackupKeep\":-1}")]
    public void Old_or_invalid_retention_settings_keep_five(string json)
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, json);

        Assert.Equal(5, AppSettings.Load(paths).SaveBackupKeep);
    }

    [Fact]
    public void The_save_backup_retention_count_survives_a_restart()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        new AppSettings { SaveBackupKeep = 20 }.Save(paths);

        Assert.Equal(20, AppSettings.Load(paths).SaveBackupKeep);
    }

    [Fact]
    public void The_language_choice_survives_a_restart_and_old_settings_still_load()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{\"backUpSavesOnLaunch\":true}");

        var settings = AppSettings.Load(paths);
        Assert.Null(settings.Language);
        Assert.True(settings.BackUpSavesOnLaunch);

        settings.Language = "zh-CN";
        settings.Save(paths);

        Assert.Equal("zh-CN", AppSettings.Load(paths).Language);
        Assert.True(AppSettings.Load(paths).BackUpSavesOnLaunch);
        Assert.Null(AppSettings.Load(paths).Window);
    }

    [Fact]
    public void The_removed_donate_button_stays_removed_and_old_settings_keep_it()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        Directory.CreateDirectory(paths.Root);
        File.WriteAllText(paths.SettingsFile, "{\"backUpSavesOnLaunch\":true}");

        var settings = AppSettings.Load(paths);
        Assert.False(settings.HideDonateButton);

        settings.HideDonateButton = true;
        settings.Save(paths);

        Assert.True(AppSettings.Load(paths).HideDonateButton);
    }

    [Fact]
    public void The_window_place_survives_a_restart()
    {
        using var folder = new TestFolder();
        var paths = new AppPaths(folder.Combine("Data"));
        var settings = new AppSettings { Window = new WindowPlacement(120.5, 40, 1400, 900, true) };

        settings.Save(paths);

        Assert.Equal(new WindowPlacement(120.5, 40, 1400, 900, true), AppSettings.Load(paths).Window);
    }
}
