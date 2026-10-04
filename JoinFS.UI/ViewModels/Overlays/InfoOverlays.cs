using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>"About JoinFS": opened from the version in the sidebar. The download link only appears when an update exists.</summary>
public sealed partial class AboutViewModel : OverlayViewModel
{
    private readonly IAppInfo _app;
    private readonly IPlatform _platform;

    public AboutViewModel(IAppInfo app, UpdateInfo? update, IPlatform platform)
    {
        _app = app;
        _platform = platform;
        Update = update;
    }

    public override string Title => "About JoinFS";

    public UpdateInfo? Update { get; }

    public string VersionText => $"Version {_app.Version}";
    public bool NewVersionAvailable => Update is not null;
    public string DownloadText => Update is null ? "" : $"Download v{Update.Version} →";
    public string License => "Licensed under the MIT License.";
    public string Copyright => _app.Copyright;

    [RelayCommand]
    private void OpenDocumentation() => _platform.OpenUrl(_app.DocumentationUrl);

    [RelayCommand]
    private void OpenDownload()
    {
        if (Update is not null)
            _platform.OpenUrl(Update.Url);
    }
}

/// <summary>"Scan For Models", opened from Settings → Simulator. Only gathers where to scan; the scan itself is not wired yet.</summary>
public sealed partial class ScanModelsViewModel : OverlayViewModel
{
    private readonly IPlatform _platform;

    public ScanModelsViewModel(bool simulatorConnected, IPlatform platform)
    {
        SimulatorConnected = simulatorConnected;
        _platform = platform;
    }

    public override string Title => "Scan For Models";

    public bool SimulatorConnected { get; }
    public string SimulatorLabel => SimulatorConnected ? "Connected" : "Not connected";

    [ObservableProperty]
    private string _rootFolder = "";

    public ObservableCollection<string> Subfolders { get; } = [];
    public ObservableCollection<string> AddOns { get; } = [];
    public ObservableCollection<string> OtherFolders { get; } = [];

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _platform.PickFolderAsync("Simulator root folder");
        if (folder is not null)
            RootFolder = folder;
    }

    // The scan needs the real model scanner and its progress; the README leaves the progress state to the wiring step.
    [RelayCommand]
    private void Scan() => Close();
}
