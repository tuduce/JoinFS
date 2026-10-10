using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>One folder under X-Plane's Aircraft folder, with the tick that includes it in the CSL generation.</summary>
public sealed partial class AircraftFolderViewModel(string name, bool isChecked) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

/// <summary>
/// "Scan For Models" in the XPLANE build (the old ScanForm_XPLANE): the X-Plane folder, the CSL folder derived from it,
/// whether to generate CSL objects and for which aircraft folders, and the scan itself.
/// </summary>
public sealed partial class ScanXPlaneModelsViewModel : OverlayViewModel
{
    private readonly ProfileViewModel _profile;
    private readonly IXPlaneScanSource _source;
    private readonly IPlatform _platform;
    private readonly HashSet<string> _ticked;

    public ScanXPlaneModelsViewModel(ProfileViewModel profile, IXPlaneScanSource source, IPlatform platform)
    {
        _profile = profile;
        _source = source;
        _platform = platform;
        _ticked = [.. source.InitialScanFolders];
        _xplaneFolder = source.SimFolder;
        RefreshFolders();
    }

    public override string Title => Loc.T("Scan for models");

    [ObservableProperty]
    private string _xplaneFolder;

    partial void OnXplaneFolderChanged(string value) => RefreshFolders();

    /// <summary>Where the generated CSL objects go: read-only, always under the X-Plane folder.</summary>
    [ObservableProperty]
    private string _cslFolder = "";

    /// <summary>The aircraft folders found under the X-Plane folder. Shown only while <see cref="GenerateCsl"/> is on.</summary>
    public ObservableCollection<AircraftFolderViewModel> AircraftFolders { get; } = [];

    public bool GenerateCsl
    {
        get => _profile.GenerateCsl;
        set
        {
            _profile.GenerateCsl = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSkipCsl));
        }
    }

    /// <summary>"Skip CSL objects already done" only means something while generating.</summary>
    public bool CanSkipCsl => GenerateCsl;

    public bool SkipCsl
    {
        get => _profile.SkipCsl;
        set
        {
            _profile.SkipCsl = value;
            OnPropertyChanged();
        }
    }

    public ProfileViewModel Profile => _profile;

    /// <summary>What the old dialog warned of before it generated CSL objects. Shown while generating.</summary>
    public static string CslWarning => Loc.T("Generating CSL objects may take several minutes. You may need to restart X-Plane when complete.");

    /// <summary>Why the scan did not start, when it needs saying. Empty otherwise.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>The aircraft folders to scan, as full paths under the X-Plane folder's Aircraft folder.</summary>
    public IReadOnlyList<string> SelectedFolderPaths =>
        [.. AircraftFolders.Where(f => f.IsChecked).Select(f => Path.Combine(XplaneFolder, "Aircraft", f.Name))];

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _platform.PickFolderAsync(Loc.T("Select the root X-Plane folder"));
        if (folder is not null)
            XplaneFolder = folder;
    }

    /// <summary>Scans the folders ticked, or all of X-Plane's aircraft when none is. The options above are already saved as they change.</summary>
    [RelayCommand]
    private void Scan()
    {
        IReadOnlyList<string> folders = [.. AircraftFolders.Where(f => f.IsChecked).Select(f => f.Name)];
        if (!_source.Scan(XplaneFolder.Trim(), folders))
        {
            Status = Loc.T("A scan is already running.");
            return;
        }
        Close();
    }

    private void RefreshFolders()
    {
        // Remember the ticks across a folder change, as the old dialog did.
        foreach (AircraftFolderViewModel folder in AircraftFolders)
        {
            if (folder.IsChecked)
                _ticked.Add(folder.Name);
            else
                _ticked.Remove(folder.Name);
        }

        CslFolder = string.IsNullOrWhiteSpace(XplaneFolder)
            ? ""
            : Path.Combine(XplaneFolder, "Resources", "plugins", "JoinFS", "Resources", "CSL");

        AircraftFolders.Clear();
        foreach (string name in _source.ListAircraftFolders(XplaneFolder))
            AircraftFolders.Add(new AircraftFolderViewModel(name, _ticked.Contains(name)));
    }
}
