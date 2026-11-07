using System.IO;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;

namespace UEManager.UI.Tests;

public sealed class DonationTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "uemm-donate-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private AppPaths Paths()
    {
        var paths = new AppPaths(Path.Combine(folder, "Data"));
        paths.EnsureWritable();
        return paths;
    }

    private static MainViewModel Model(AppPaths paths) =>
        new(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths, new ArtSet(null, null));

    /// <summary>What waits for the answer in the window goes on.</summary>
    private static void Settle() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    [Fact]
    public void The_button_shows_until_the_player_removes_it() => UiThread.Run(() =>
        Assert.True(Model(Paths()).ShowDonateButton));

    [Fact]
    public void Cancel_keeps_the_button() => UiThread.Run(() =>
    {
        var paths = Paths();
        var model = Model(paths);

        model.RemoveDonateButtonCommand.Execute(null);
        Assert.Equal("Remove the Donate button?", model.Confirm!.Title);
        model.Confirm.CancelCommand.Execute(null);
        Settle();

        Assert.Null(model.Confirm);
        Assert.True(model.ShowDonateButton);
        Assert.False(AppSettings.Load(paths).HideDonateButton);
    });

    [Fact]
    public void Confirm_removes_the_button_for_good() => UiThread.Run(() =>
    {
        var paths = Paths();
        var model = Model(paths);

        model.RemoveDonateButtonCommand.Execute(null);
        model.Confirm!.AcceptCommand.Execute(null);
        Settle();

        Assert.False(model.ShowDonateButton);
        Assert.True(AppSettings.Load(paths).HideDonateButton);
        Assert.False(Model(paths).ShowDonateButton);
    });

    [Fact]
    public void The_thank_you_window_opens_and_closes_and_covers_the_window() => UiThread.Run(() =>
    {
        var model = Model(Paths());

        model.ShowDonationCommand.Execute(null);
        Assert.True(model.IsDonationOpen);
        Assert.True(model.HasOverlay);

        model.CloseDonationCommand.Execute(null);
        Assert.False(model.IsDonationOpen);
        Assert.False(model.HasOverlay);
    });

    [Fact]
    public void The_thank_you_window_says_it_is_optional() => UiThread.Run(() =>
        Assert.Contains("fully optional", Model(Paths()).DonationOptionalText));

    [Fact]
    public void The_link_goes_to_paypal_over_https() =>
        Assert.StartsWith("https://www.paypal.com/donate/", AppInfo.DonateUrl);
}
