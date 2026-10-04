using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>"Install X-Plane Plugin" (the old XPlaneForm): pick X-Plane's folder, then install the plugin into it. XPLANE build only.</summary>
public sealed partial class InstallXPlanePluginViewModel : OverlayViewModel
{
    private readonly IXPlanePluginInstaller _installer;
    private readonly IPlatform _platform;

    public InstallXPlanePluginViewModel(IXPlanePluginInstaller installer, IPlatform platform)
    {
        _installer = installer;
        _platform = platform;
        _folder = installer.SavedFolder;
    }

    public override string Title => "Install X-Plane Plugin";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private string _folder;

    /// <summary>Why the last install failed, or null.</summary>
    [ObservableProperty]
    private string? _error;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? picked = await _platform.PickFolderAsync("X-Plane folder");
        if (picked is not null)
            Folder = picked;
    }

    private bool CanInstall => !string.IsNullOrWhiteSpace(Folder);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        try
        {
            await _installer.InstallAsync(Folder.Trim(), CancellationToken.None);
            Close();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
