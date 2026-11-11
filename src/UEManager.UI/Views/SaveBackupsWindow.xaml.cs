using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using UEManager.UI.ViewModels;

namespace UEManager.UI.Views;

/// <summary>The full backup list, in the same game theme as Settings.</summary>
public partial class SaveBackupsWindow : Window
{
    public SaveBackupsWindow(MainViewModel model)
    {
        InitializeComponent();
        DataContext = model;
        var size = MainWindow.Smallest(new Size(Width, Height), SystemParameters.WorkArea);
        Width = size.Width;
        Height = size.Height;
        MinWidth = Math.Min(MinWidth, Width);
        MinHeight = Math.Min(MinHeight, Height);
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized
            ? SystemParameters.WindowResizeBorderThickness : new Thickness(0);
        PropertyChangedEventManager.AddHandler(model, OnModelChanged, nameof(model.Confirm));
        Closed += (_, _) => PropertyChangedEventManager.RemoveHandler(model, OnModelChanged, nameof(model.Confirm));
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (DataContext is MainViewModel { Confirm: not null })
            Dispatcher.BeginInvoke(() => ConfirmCancel.Focus(), DispatcherPriority.Input);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DataContext is MainViewModel model)
        {
            // Finish file operations before returning to Settings; a pending question is cancelled first.
            e.Cancel = model.IsBusy || model.Confirm is not null;
            model.Confirm?.CancelCommand.Execute(null);
        }
        base.OnClosing(e);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
