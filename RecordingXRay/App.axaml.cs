using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RecordingXRay.Services;
using RecordingXRay.ViewModels;
using RecordingXRay.Views;

namespace RecordingXRay;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainViewModel viewModel = new(
                tiles: new OsmTileSource(OsmTileSource.DefaultCacheDirectory),
                settingsStore: new FileSettingsStore(FileSettingsStore.DefaultPath));
            desktop.MainWindow = new MainWindow { DataContext = viewModel };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
