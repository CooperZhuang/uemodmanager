using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using UEManager.Core.Games;
using UEManager.Core.Mods;
using UEManager.Core.Storage;
using UEManager.UI.Services;
using UEManager.UI.ViewModels;
using Xunit;
using UEManager.UI.Catalog;
using UEManager.UI.Views;

namespace UEManager.UI.Tests;

[Collection("Language")]
public class LanguageSwitchTests
{
    [Fact]
    public void Existing_window_switches_languages_without_losing_state_and_cached_errors_follow_it()
    {
        Exception? failure = null;
        var stage = "starting the STA thread";
        var thread = new Thread(() =>
        {
            try { VerifySwitch(value => stage = value); }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(30)), $"The WPF language check did not finish: {stage}.");
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void VerifySwitch(Action<string> stage)
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var originalLanguage = Localizer.Current.Language;
        var originalFont = LanguageFonts.Font;
        var originalUiScale = LanguageFonts.UiScale;
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefault = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUi = CultureInfo.DefaultThreadCurrentUICulture;
        var folder = Path.Combine(Path.GetTempPath(), "uemm-ui-test-" + Guid.NewGuid().ToString("N"));
        // Use an inert WPF host: constructing the real App queues its production startup on the dispatcher.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        MainWindow? window = null;
        try
        {
            stage("loading application resources");
            foreach (var resource in new[] { "Theme/Palette.xaml", "Theme/Controls.xaml" })
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri($"/UEModManager;component/{resource}", UriKind.Relative),
                });
            app.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("/UEModManager;component/Catalog/CatalogResources.xaml", UriKind.Relative),
            });
            Localizer.Current.SetLanguage("en");
            var paths = new AppPaths(folder);
            paths.EnsureWritable();
            stage("creating the view model");
            var model = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths,
                new ArtSet(null, null));
            model.SelectedPage = "Settings";
            model.SearchText = "蓝图";
            var row = new ModItemViewModel(model, new ModRecord
            {
                Id = "blueprint",
                Name = "User mod {0} 中文",
                Files = [new ModFileRecord { Target = DeployTarget.LogicMods, FileName = "User.pak" }],
            }, Path.Combine(folder, "library"));
            model.Mods.Add(row);
            model.ModsView.Refresh();
            Assert.True(model.ModsView.IsEmpty);

            stage("constructing the window");
            window = new MainWindow { DataContext = model };
            FlushBindings();
            var surface = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
            stage("laying out the window");
            surface.Measure(new Size(1360, 800));
            surface.Arrange(new Rect(0, 0, 1360, 800));
            FlushBindings();
            var selectors = Descendants(surface).OfType<ComboBox>().ToList();
            Assert.Equal(2, selectors.Count);
            var selector = selectors.Single(c => ReferenceEquals(c.ItemsSource, model.Languages));
            var fontSelector = selectors.Single(c => ReferenceEquals(c.ItemsSource, model.Fonts));
            Assert.Equal("en", selector.SelectedValue);
            Assert.Equal(LanguageFonts.Default, fontSelector.SelectedValue);
            Assert.Contains(Descendants(surface).OfType<TextBlock>(), t => t.Text == "Settings");

            // The control changes the existing model and saves the choice, without replacing the window or page.
            stage("switching to Chinese");
            selector.SelectedValue = "zh-CN";
            FlushBindings();
            Assert.Same(model, window.DataContext);
            Assert.Equal("Settings", model.SelectedPage);
            Assert.Equal("蓝图", model.SearchText);
            Assert.Equal("zh-CN", AppSettings.Load(paths).Language);
            Assert.Equal("主题默认", Assert.IsType<FontOption>(fontSelector.SelectedItem).Name);
            Assert.Contains(Descendants(surface).OfType<TextBlock>(), t => t.Text == "设置");
            Assert.Equal("zh-CN", window.Language.IetfLanguageTag, ignoreCase: true);
            Assert.Equal("影之刃零", model.GameDisplayName);
            Assert.Equal("User mod {0} 中文", row.Name);
            Assert.Equal("蓝图", row.KindText);
            Assert.Single(model.ModsView.Cast<ModItemViewModel>());
            Assert.True(row.Matches("Blueprint"));

            using var http = new HttpClient(new MissingHandler());
            var update = new AppUpdateViewModel(model, http, paths, ["https://example.invalid/latest"], AppInfo.ReleaseFileName);
            stage("checking an unavailable release");
            WaitFor(update.CheckAsync());
            Assert.Equal("尚无发布版本。", update.StatusHint);
            var catalog = new CatalogViewModel(model, http, "https://example.invalid/catalog", null, true,
                CatalogSortField.Name, false);
            stage("loading an unavailable catalog");
            WaitFor(catalog.LoadAsync());
            Assert.StartsWith("测试列表：", catalog.TestCatalogText);
            Assert.StartsWith("无法加载 openmods 列表：", catalog.Error);
            stage("switching back to English");
            selector.SelectedValue = "en";
            FlushBindings();
            Assert.Equal("en", AppSettings.Load(paths).Language);
            Assert.Equal("Settings", model.SelectedPage);
            Assert.Equal("蓝图", model.SearchText);
            Assert.True(model.ModsView.IsEmpty);
            Assert.Contains(Descendants(surface).OfType<TextBlock>(), t => t.Text == "Settings");
            Assert.Equal("No release is published yet.", update.StatusHint);
            Assert.StartsWith("Test list:", catalog.TestCatalogText);
            Assert.StartsWith("The openmods list could not be loaded:", catalog.Error);

            stage("checking the interface scale slider");
            VerifyUiScaleSlider(app, surface, model, paths);

            stage("checking font selection, popup scaling, tooltip scaling and reset");
            VerifyFontSelectionAndReset(app, surface, model, selector, fontSelector, paths);

            stage("checking responsive toolbars and dialogs at maximum scale");
            VerifyResponsiveLayoutsAtMaxScale(stage, app, surface, model, catalog, update, folder);
        }
        finally
        {
            stage("closing the window");
            window?.Close();
            Localizer.Current.SetLanguage(originalLanguage);
            LanguageFonts.SetFont(originalFont);
            LanguageFonts.SetUiScale(originalUiScale);
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefault;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUi;
            stage("shutting down the test application");
            app.Shutdown();
            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }

    private static void VerifyUiScaleSlider(Application app, FrameworkElement surface, MainViewModel model, AppPaths paths)
    {
        LanguageFonts.SetUiScale(model.UiScale);
        LanguageFonts.Follow(app.Resources);
        var slider = Descendants(surface).OfType<Slider>().Single(s => s.Name == "UiScaleSlider");
        var label = Descendants(VisualTreeHelper.GetParent(slider)).OfType<TextBlock>().Single();
        Assert.Equal(100, slider.Value);
        Assert.Equal("100%", label.Text);

        // Dragging previews the numeric value without scaling the UI or writing settings until DragCompleted.
        slider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        slider.Value = 140;
        FlushBindings();
        Assert.Equal("140%", label.Text);
        Assert.Equal(100, model.UiScale);
        Assert.Equal(100, AppSettings.Load(paths).UiScale);
        Assert.Equal(1.0, Assert.IsType<ScaleTransform>(app.Resources["Font.Scale"]).ScaleX, precision: 6);

        slider.Value = 115;
        FlushBindings();
        Assert.Equal("115%", label.Text);
        Assert.Equal(100, model.UiScale);

        slider.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled: false) { RoutedEvent = Thumb.DragCompletedEvent });
        FlushBindings();
        Assert.Equal(115, model.UiScale);
        Assert.Equal(115, AppSettings.Load(paths).UiScale);
        Assert.Equal(1.15, Assert.IsType<ScaleTransform>(app.Resources["Font.Scale"]).ScaleX, precision: 6);

        // Pressing on the track also waits until the pointer is released.
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
        });
        slider.Value = 130;
        FlushBindings();
        Assert.Equal("130%", label.Text);
        Assert.Equal(115, model.UiScale);
        slider.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent,
        });
        FlushBindings();
        Assert.Equal(130, model.UiScale);
        Assert.Equal(130, AppSettings.Load(paths).UiScale);

        // Non-pointer changes (such as keyboard navigation) still apply immediately.
        slider.Value = 95;
        FlushBindings();
        Assert.Equal(95, model.UiScale);
        Assert.Equal(95, AppSettings.Load(paths).UiScale);
        Assert.Equal(0.95, Assert.IsType<ScaleTransform>(app.Resources["Font.Scale"]).ScaleX, precision: 6);

        // Binding Minimum=85 before Value=125 on a fresh window must not overwrite the saved UiScale.
        var savedModel = new MainViewModel(new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" }, paths,
            new ArtSet(null, null)) { SelectedPage = "Settings" };
        savedModel.UiScale = 125;
        var secondWindow = new MainWindow { DataContext = savedModel };
        try
        {
            FlushBindings();
            var secondSurface = Assert.IsAssignableFrom<FrameworkElement>(secondWindow.Content);
            secondSurface.Measure(new Size(1360, 800));
            secondSurface.Arrange(new Rect(0, 0, 1360, 800));
            FlushBindings();
            var secondSlider = Descendants(secondSurface).OfType<Slider>().Single(s => s.Name == "UiScaleSlider");
            var secondLabel = Descendants(VisualTreeHelper.GetParent(secondSlider)).OfType<TextBlock>().Single();
            Assert.Equal(125, secondSlider.Value);
            Assert.Equal(125, savedModel.UiScale);
            Assert.Equal("125%", secondLabel.Text);
        }
        finally
        {
            secondWindow.Close();
        }
    }

    private static void VerifyFontSelectionAndReset(Application app, FrameworkElement surface, MainViewModel model,
        ComboBox selector, ComboBox fontSelector, AppPaths paths)
    {
        model.SelectedFont = LanguageFonts.Default;
        model.UiScale = AppSettings.DefaultUiScale;
        FlushBindings();

        var resetButton = Descendants(surface).OfType<Button>()
            .Single(b => ReferenceEquals(b.Command, model.ResetFontSettingsCommand));
        Assert.False(model.CanResetFontSettings);
        Assert.Equal(Visibility.Collapsed, resetButton.Visibility);
        Assert.Equal("Name", TextSearch.GetTextPath(fontSelector));

        fontSelector.ApplyTemplate();
        var headerText = Assert.IsType<TextBlock>(Descendants(fontSelector).FirstOrDefault(d =>
            d is TextBlock tb && tb.TextTrimming == TextTrimming.CharacterEllipsis));
        var popup = Assert.IsType<Popup>(fontSelector.Template.FindName("PART_Popup", fontSelector));
        var popupBorder = Assert.IsType<Border>(popup.Child);
        Assert.Same(app.Resources["Font.Scale"], popupBorder.LayoutTransform);

        var tooltip = new ToolTip { Content = "Font scaling check" };
        tooltip.ApplyTemplate();
        Assert.Equal(app.Resources["Font.Body"], tooltip.FontFamily);
        var tooltipBorder = Assert.IsType<Border>(VisualTreeHelper.GetChild(tooltip, 0));
        Assert.Same(app.Resources["Font.Scale"], tooltipBorder.LayoutTransform);

        fontSelector.SelectedValue = "Consolas";
        FlushBindings();
        Assert.Equal("Consolas", model.SelectedFont);
        Assert.Equal("Consolas", LanguageFonts.Font);
        Assert.Equal("Consolas", AppSettings.Load(paths).Font);
        Assert.Equal("Consolas, Microsoft YaHei UI", Assert.IsType<FontFamily>(app.Resources["Font.Body"]).Source);
        Assert.Equal("Consolas, Microsoft YaHei UI", Assert.IsType<FontOption>(fontSelector.SelectedItem).PreviewFont.Source);
        Assert.True(model.CanResetFontSettings);
        Assert.Equal(Visibility.Visible, resetButton.Visibility);

        // Switching language preserves the selected font, updates fallback, and re-sorts font options in place.
        selector.SelectedValue = "zh-CN";
        FlushBindings();
        Assert.Equal("Consolas", model.SelectedFont);
        Assert.Equal("Consolas", fontSelector.SelectedValue);
        Assert.Equal(
            LanguageFonts.Options().Skip(2).Select(o => o.Code),
            model.Fonts.Skip(2).Select(o => o.Code));

        selector.SelectedValue = "ja";
        FlushBindings();
        Assert.Equal("Consolas, Yu Gothic UI", Assert.IsType<FontFamily>(app.Resources["Font.Body"]).Source);

        selector.SelectedValue = "en";
        FlushBindings();
        Assert.Equal("Consolas", model.SelectedFont);

        model.UiScale = AppSettings.MaxUiScale;
        FlushBindings();
        var maxScale = Assert.IsType<ScaleTransform>(app.Resources["Font.Scale"]);
        Assert.Equal(1.4, maxScale.ScaleX, precision: 6);
        Assert.Same(maxScale, popupBorder.LayoutTransform);

        resetButton.Command.Execute(resetButton.CommandParameter);
        FlushBindings();
        Assert.Equal(LanguageFonts.Default, model.SelectedFont);
        Assert.Equal(AppSettings.DefaultUiScale, model.UiScale);
        Assert.Null(LanguageFonts.Font);
        Assert.Equal(AppSettings.DefaultUiScale, LanguageFonts.UiScale);
        Assert.Null(AppSettings.Load(paths).Font);
        Assert.Equal(AppSettings.DefaultUiScale, AppSettings.Load(paths).UiScale);
        Assert.False(model.CanResetFontSettings);
        Assert.Equal(Visibility.Collapsed, resetButton.Visibility);
        Assert.Equal(1.0, Assert.IsType<ScaleTransform>(app.Resources["Font.Scale"]).ScaleX, precision: 6);
    }

    private static void VerifyResponsiveLayoutsAtMaxScale(Action<string> stage, Application app, FrameworkElement surface,
        MainViewModel model, CatalogViewModel catalog, AppUpdateViewModel update, string folder)
    {
        var previousPage = model.SelectedPage;
        try
        {
            stage("checking ModsView responsive toolbar");
            model.SelectedPage = "Mods";
            model.UiScale = AppSettings.DefaultUiScale;
            surface.Measure(new Size(1360, 800));
            surface.Arrange(new Rect(0, 0, 1360, 800));
            FlushBindings();

            var modsView = Descendants(surface).OfType<ModsView>().Single();
            var modsActions = Assert.IsType<StackPanel>(modsView.FindName("ToolbarActions"));
            var modsSearch = Assert.IsType<TextBox>(modsView.FindName("SearchBox"));
            Assert.Equal(0, Grid.GetRow(modsActions));

            model.UiScale = AppSettings.MaxUiScale;
            surface.Measure(new Size(1360, 800));
            surface.Arrange(new Rect(0, 0, 1360, 800));
            FlushBindings();
            Assert.Equal(1, Grid.GetRow(modsActions));
            Assert.True(modsSearch.ActualWidth > 300, $"Expected ModsView SearchBox to stay wide, actual {modsSearch.ActualWidth}.");

            stage("checking CatalogToolbar responsive wrap");
            var catalogToolbar = new CatalogToolbar { DataContext = catalog };
            var toolbarHost = new Grid { LayoutTransform = LanguageFonts.ScaleFor(AppSettings.DefaultUiScale) };
            toolbarHost.Children.Add(catalogToolbar);
            toolbarHost.Measure(new Size(1120, 200));
            toolbarHost.Arrange(new Rect(0, 0, 1120, 200));
            FlushBindings();
            var controlsBox = Assert.IsType<Border>(catalogToolbar.FindName("ControlsBox"));
            var catalogSearch = Assert.IsType<TextBox>(catalogToolbar.FindName("SearchBox"));
            Assert.Equal(0, Grid.GetRow(controlsBox));

            toolbarHost.LayoutTransform = LanguageFonts.ScaleFor(AppSettings.MaxUiScale);
            toolbarHost.Measure(new Size(1120, 200));
            toolbarHost.Arrange(new Rect(0, 0, 1120, 200));
            FlushBindings();
            Assert.Equal(1, Grid.GetRow(controlsBox));
            Assert.True(catalogSearch.ActualWidth > 300, $"Expected CatalogToolbar SearchBox to stay wide, actual {catalogSearch.ActualWidth}.");

            stage("checking dialogs inside 1280x720 at maximum scale");
            var iniPath = Path.Combine(folder, "sample.ini");
            File.WriteAllLines(iniPath, Enumerable.Range(1, 40).Select(i => $"Key{i}=Value{i}"));
            var settingsEditorVm = new SettingsEditorViewModel(model, "Sample Mod", [new SettingsFileItem(iniPath, "Sample.ini")]);
            var helpReportVm = new HelpReportViewModel(model, string.Join('\n', Enumerable.Range(1, 30).Select(i => $"Diagnostic line {i}")));
            var modSetsVm = new ModSetsViewModel(model);
            var gameProfile = new GameProfile { Id = "test", DisplayName = "Phantom Blade Zero" };
            var install = new GameInstall(gameProfile, folder, null, folder, folder, folder, GameStore.Steam);
            var ue4ssGuideVm = new Ue4ssGuideViewModel(model, install, null);
            var modRecord = new ModRecord
            {
                Id = "sample",
                Name = "Sample Mod",
                Files = [new ModFileRecord { Target = DeployTarget.LogicMods, FileName = "Sample.pak" }],
            };
            var files = Enumerable.Range(1, 20)
                .Select(i => new ModFileItem($"LogicMods/Sub{i}/File{i}.pak", Path.Combine(folder, $"File{i}.pak"), "1.2 MB", "Installed", "Success"))
                .ToList();
            var modDetailsVm = new ModDetailsViewModel(model, modRecord, files, [], folder);

            var dialogs = new FrameworkElement[]
            {
                new SettingsEditor { DataContext = settingsEditorVm },
                new HelpReportDialog { DataContext = helpReportVm },
                new CatalogTypesView { DataContext = catalog },
                new CatalogAboutView { DataContext = catalog },
                new ChangelogDialog { DataContext = update },
                new UpdateDialog { DataContext = update },
                new ModSetsDialog { DataContext = modSetsVm },
                new Ue4ssGuide { DataContext = ue4ssGuideVm },
                new ModDetailsDialog { DataContext = modDetailsVm },
            };

            const double viewportWidth = 1280;
            const double viewportHeight = 720 - 34;
            foreach (var dialog in dialogs)
            {
                stage($"measuring {dialog.GetType().Name} at 1280x720 and 140% scale");
                dialog.Margin = new Thickness(40);
                var overlay = new Grid { Children = { dialog } };
                var scaledHost = new Grid
                {
                    LayoutTransform = LanguageFonts.ScaleFor(AppSettings.MaxUiScale),
                    Children = { overlay },
                };
                scaledHost.Measure(new Size(viewportWidth, viewportHeight));
                scaledHost.Arrange(new Rect(0, 0, viewportWidth, viewportHeight));
                FlushBindings();

                Assert.True(dialog.ActualWidth <= overlay.ActualWidth - 80 + 0.5,
                    $"{dialog.GetType().Name} overflowed width: {dialog.ActualWidth} > {overlay.ActualWidth - 80}.");
                Assert.True(dialog.ActualHeight <= overlay.ActualHeight - 80 + 0.5,
                    $"{dialog.GetType().Name} overflowed height: {dialog.ActualHeight} > {overlay.ActualHeight - 80}.");

                foreach (var button in Descendants(dialog).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0 && b.ActualHeight > 0))
                {
                    var bounds = button.TransformToAncestor(scaledHost)
                        .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    Assert.True(bounds.Left >= -0.5 && bounds.Top >= -0.5
                                && bounds.Right <= viewportWidth + 0.5
                                && bounds.Bottom <= viewportHeight + 0.5,
                        $"{dialog.GetType().Name} clipped button '{button.Content}': {bounds}.");
                }
            }

            stage("measuring ResetModdingView at 1280x720 and 140% scale");
            var resetVm = new ResetModdingViewModel("Phantom Blade Zero", new ResetPreview(2, 3, true, 1));
            resetVm.Start();
            resetVm.Finish("Modding reset: 2 mods turned off, UE4SS removed, 3 other files moved to a backup folder.", failed: false);
            var resetView = new ResetModdingView { DataContext = resetVm };
            var resetHost = new Grid
            {
                LayoutTransform = LanguageFonts.ScaleFor(AppSettings.MaxUiScale),
                Children = { resetView },
            };
            resetHost.Measure(new Size(viewportWidth, viewportHeight));
            resetHost.Arrange(new Rect(0, 0, viewportWidth, viewportHeight));
            FlushBindings();
            var resetViewer = Assert.IsAssignableFrom<FrameworkElement>(resetView.FindName("Viewer"));
            Assert.True(resetViewer.ActualHeight >= 60,
                $"Expected ResetModdingView log viewer to stay usable at 140% scale, actual height {resetViewer.ActualHeight}.");
            foreach (var button in Descendants(resetView).OfType<Button>().Where(b => b.IsVisible && b.ActualWidth > 0 && b.ActualHeight > 0))
            {
                var bounds = button.TransformToAncestor(resetHost)
                    .TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                Assert.True(bounds.Left >= -0.5 && bounds.Top >= -0.5
                            && bounds.Right <= viewportWidth + 0.5
                            && bounds.Bottom <= viewportHeight + 0.5,
                    $"ResetModdingView clipped button '{button.Content}': {bounds}.");
            }
        }
        finally
        {
            model.SelectedPage = previousPage;
            model.SelectedFont = LanguageFonts.Default;
            model.UiScale = AppSettings.DefaultUiScale;
        }
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static void FlushBindings() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void WaitFor(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private sealed class MissingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound) { ReasonPhrase = "Not Found" });
    }
}
