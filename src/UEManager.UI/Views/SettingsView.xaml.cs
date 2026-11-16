using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace UEManager.UI.Views;

public partial class SettingsView : UserControl
{
    private bool isDraggingUiScale;
    private bool isUiScalePointerDown;

    public SettingsView() => InitializeComponent();

    private void OnFontListOpened(object sender, EventArgs e) => (DataContext as ViewModels.MainViewModel)?.LoadAllFonts();

    private void OnFontListFocused(object sender, KeyboardFocusChangedEventArgs e) => (DataContext as ViewModels.MainViewModel)?.LoadAllFonts();

    private void OnUiScaleDragStarted(object sender, DragStartedEventArgs e) => isDraggingUiScale = true;

    private void OnUiScaleDragCompleted(object sender, DragCompletedEventArgs e)
    {
        isDraggingUiScale = false;
        isUiScalePointerDown = false;
        CommitUiScale();
    }

    private void OnUiScalePointerDown(object sender, MouseButtonEventArgs e) => isUiScalePointerDown = true;

    private void OnUiScalePointerUp(object sender, MouseButtonEventArgs e)
    {
        isUiScalePointerDown = false;
        if (!isDraggingUiScale)
            CommitUiScale();
    }

    private void OnUiScaleLostMouseCapture(object sender, MouseEventArgs e)
    {
        if ((!isDraggingUiScale && !isUiScalePointerDown) || UiScaleSlider.IsMouseCaptureWithin)
            return;

        isDraggingUiScale = false;
        isUiScalePointerDown = false;
        CommitUiScale();
    }

    private void OnUiScaleMouseLeave(object sender, MouseEventArgs e) => ClearStalePointerDown();

    private void OnUiScalePreviewKeyDown(object sender, KeyEventArgs e) => ClearStalePointerDown();

    private void OnUiScaleLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => ClearStalePointerDown();

    private void ClearStalePointerDown()
    {
        if (isUiScalePointerDown && !UiScaleSlider.IsMouseCaptureWithin && Mouse.LeftButton == MouseButtonState.Released)
            isUiScalePointerDown = false;
    }

    private void OnUiScaleValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (isDraggingUiScale || isUiScalePointerDown)
            return;

        // Ignore binding-initialization coercion when Minimum rises above the initial default 0 before Value binds.
        if (UiScaleSlider.Minimum >= UiScaleSlider.Maximum || e.OldValue < UiScaleSlider.Minimum)
            return;

        CommitUiScale();
    }

    /// <summary>Pushes the slider's chosen interface scale to the view model so UI scaling and persistence run once.</summary>
    private void CommitUiScale()
    {
        if (UiScaleSlider.Minimum >= UiScaleSlider.Maximum)
            return;

        UiScaleSlider.GetBindingExpression(RangeBase.ValueProperty)?.UpdateSource();
    }
}
