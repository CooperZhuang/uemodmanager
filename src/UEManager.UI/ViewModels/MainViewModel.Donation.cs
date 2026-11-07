using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UEManager.UI.Services;

namespace UEManager.UI.ViewModels;

/// <summary>The Donate button: in the sidebar until the player removes it, and always on the About page.</summary>
public sealed partial class MainViewModel
{
    /// <summary>The thank-you window is open.</summary>
    [ObservableProperty]
    private bool isDonationOpen;

    /// <summary>The player has not removed the button from the sidebar.</summary>
    public bool ShowDonateButton => !settings.HideDonateButton;

    public string DonationOptionalText => L.T("If you would like to support the work, a donation is a kind way to say thanks. It is fully optional, so please do not feel obliged to do so. Only if you feel generous <3");

    [RelayCommand]
    private void ShowDonation() => IsDonationOpen = true;

    [RelayCommand]
    private void CloseDonation() => IsDonationOpen = false;

    [RelayCommand]
    private void Donate()
    {
        IsDonationOpen = false;
        TryOpen(AppInfo.DonateUrl);
    }

    /// <summary>The X on the button: it asks first, and the choice stays in the settings.</summary>
    [RelayCommand]
    private async Task RemoveDonateButton()
    {
        if (!await AskAsync(L.T("Remove the Donate button?"),
                L.T("This removes the button from the sidebar for good. You can still find it at the top of the About page. Nothing else changes."),
                L.T("Remove button")))
            return;
        settings.HideDonateButton = true;
        SaveSettings();
        OnPropertyChanged(nameof(ShowDonateButton));
    }
}
