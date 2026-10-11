using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>
/// "Install X-Plane Plugin" (the old XPlaneForm): the installation to put the plugin into, then the install. XPLANE build only.
/// The folder starts on the installation in use or the first one found, so most users only press Install.
/// </summary>
public sealed partial class InstallXPlanePluginViewModel : OverlayViewModel
{
    private readonly IXPlanePluginInstaller _installer;
    private readonly IPlatform _platform;

    public InstallXPlanePluginViewModel(IXPlanePluginInstaller installer, IPlatform platform)
    {
        _installer = installer;
        _platform = platform;
        Installs = installer.Installs;
        _folder = installer.SavedFolder;
        SelectedInstall = FindInstall(_folder);
    }

    public override string Title => Loc.T("Install X-Plane Plugin");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private string _folder;

    partial void OnFolderChanged(string value)
    {
        Error = null;
        SelectedInstall = FindInstall(value);
    }

    /// <summary>Every X-Plane installation found, to pick from. The folder box stays the one value.</summary>
    public IReadOnlyList<XPlaneInstallInfo> Installs { get; }

    /// <summary>The picker is only worth showing when there is a choice.</summary>
    public bool HasInstallChoice => Installs.Count >= 2;

    [ObservableProperty]
    private XPlaneInstallInfo? _selectedInstall;

    partial void OnSelectedInstallChanged(XPlaneInstallInfo? value)
    {
        if (value is not null)
            Folder = value.Path;
    }

    private XPlaneInstallInfo? FindInstall(string folder) => Installs.FirstOrDefault(i => XPlaneFolderText.Same(i.Path, folder));

    /// <summary>Why the last install failed, or null.</summary>
    [ObservableProperty]
    private string? _error;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? picked = await _platform.PickFolderAsync(Loc.T("X-Plane folder"));
        if (picked is not null)
            Folder = picked;
    }

    private bool CanInstall => !string.IsNullOrWhiteSpace(Folder);

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        string folder = Folder.Trim();
        if (!_installer.IsValidInstall(folder))
        {
            Error = XPlaneFolderText.NotAnInstall;
            return;
        }

        try
        {
            await _installer.InstallAsync(folder, CancellationToken.None);
            Close();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
    }
}
