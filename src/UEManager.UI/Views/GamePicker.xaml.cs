using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

public partial class GamePicker : UserControl
{
    /// <summary>How long a panel (the developer's message, the missing game) takes to slide open, and to slide shut.</summary>
    private static readonly TimeSpan SlideOpen = TimeSpan.FromSeconds(0.5);

    private static readonly TimeSpan SlideShut = TimeSpan.FromSeconds(0.3);

    private GamePickerViewModel? picker;

    public GamePicker()
    {
        InitializeComponent();
        // The picker takes the keys as soon as it shows, away from the view behind it: Tab and the arrow keys then
        // move between the cards, and Enter chooses. No card is marked before a key is pressed.
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
                Dispatcher.BeginInvoke(() => Focus(), DispatcherPriority.Input);
        };
        DataContextChanged += (_, e) =>
        {
            if (picker is not null)
                picker.PropertyChanged -= OnPickerChanged;
            picker = e.NewValue as GamePickerViewModel;
            if (picker is null)
                return;
            picker.PropertyChanged += OnPickerChanged;
            // As the picker shows: the panels as they are, without a slide, so at the very first start the message is
            // simply open.
            ShowPanel(Message, picker.IsMessageOpen, slide: false, () => false);
            ShowPanel(MissingGame, picker.IsMissingGameOpen, slide: false, () => false);
        };
    }

    /// <summary>A click on a link: its panel slides open, or shut.</summary>
    private void OnPickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not GamePickerViewModel changed)
            return;
        if (e.PropertyName == nameof(GamePickerViewModel.IsMessageOpen))
            ShowPanel(Message, changed.IsMessageOpen, slide: true, () => changed.IsMessageOpen);
        else if (e.PropertyName == nameof(GamePickerViewModel.IsMissingGameOpen))
            ShowPanel(MissingGame, changed.IsMissingGameOpen, slide: true, () => changed.IsMissingGameOpen);
    }

    /// <summary>
    /// Opens or closes a panel: the developer's message, or the one for a missing game. A slide grows or shrinks its
    /// height, so the cards below move with it; without one, the panel is there or gone at once.
    /// </summary>
    /// <param name="isOpen">Whether the panel is open by now: a shut slide hides it only when it was not opened again.</param>
    private static void ShowPanel(Border panel, bool open, bool slide, Func<bool> isOpen)
    {
        var scale = (ScaleTransform)panel.LayoutTransform;
        if (!slide)
        {
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            panel.BeginAnimation(OpacityProperty, null);
            scale.ScaleY = open ? 1 : 0;
            panel.Opacity = open ? 1 : 0;
            panel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            return;
        }
        var time = open ? SlideOpen : SlideShut;
        var height = new DoubleAnimation(open ? 1 : 0, time)
        {
            EasingFunction = new CubicEase { EasingMode = open ? EasingMode.EaseOut : EasingMode.EaseIn },
        };
        if (open)
            panel.Visibility = Visibility.Visible;
        else
            // Gone from the keys' way once shut, unless the player opened it again meanwhile.
            height.Completed += (_, _) =>
            {
                if (!isOpen())
                    panel.Visibility = Visibility.Collapsed;
            };
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, height);
        panel.BeginAnimation(OpacityProperty, new DoubleAnimation(open ? 1 : 0, time));
    }
}
