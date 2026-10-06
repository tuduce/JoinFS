using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
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

    public override string Title => Loc.T("About JoinFS");

    public UpdateInfo? Update { get; }

    public string VersionText => Loc.F("Version {0}", _app.Version);
    public bool NewVersionAvailable => Update is not null;
    public string DownloadText => Update is null ? "" : Loc.F("Download v{0} →", Update.Version);
    public string License => Loc.T("Licensed under the MIT License.");
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
