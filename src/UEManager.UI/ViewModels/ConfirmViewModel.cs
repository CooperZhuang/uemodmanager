using CommunityToolkit.Mvvm.Input;

namespace UEManager.UI.ViewModels;

/// <summary>A yes/no question shown in the themed overlay instead of a system message box.</summary>
public sealed partial class ConfirmViewModel(string title, string message, string confirmText, string cancelText, double width = 480) : LocalizedObservableObject
{
    private readonly TaskCompletionSource<bool> result = new();

    public string Title => title;

    public string Message => message;

    public string ConfirmText => confirmText;

    public string CancelText => cancelText;

    public double Width => width;

    public Task<bool> Result => result.Task;

    /// <summary>
    /// The question that offers a player who chose no language yet the language of Windows. It is in that language and
    /// in English; Yes and No stay English, which everyone reads.
    /// </summary>
    public static ConfirmViewModel LanguageOffer(LanguageOption offered) => new(
        L.Both(offered, "Language", "  ·  "),
        L.Both(offered, "Switch the app to {0}?") + "\n\n" + L.Both(offered, "You can change this later in Settings ▸ Language."),
        "Yes", "No");

    [RelayCommand]
    private void Accept() => result.TrySetResult(true);

    [RelayCommand]
    private void Cancel() => result.TrySetResult(false);
}
