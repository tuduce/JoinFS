using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>What the X-Plane cards say about a folder that is not an X-Plane installation.</summary>
internal static class XPlaneFolderText
{
    public static string NotAnInstall => Loc.T("This is not an X-Plane folder. Select the folder that contains \"Aircraft\" and \"Resources\".");

    /// <summary>True when both folders are the same place, whatever the case and a trailing separator.</summary>
    public static bool Same(string? a, string? b) =>
        string.Equals((a ?? "").Trim().TrimEnd('\\', '/'), (b ?? "").Trim().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>One folder under X-Plane's Aircraft folder, with the tick that includes it in the CSL generation.</summary>
public sealed partial class AircraftFolderViewModel(string name, bool isChecked, Action? changed = null) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isChecked = isChecked;

    partial void OnIsCheckedChanged(bool value) => changed?.Invoke();
}

/// <summary>An installed CSL pack, with the tick that links it into JoinFS's CSL folder.</summary>
public sealed partial class CslPackViewModel(string name, int packages, bool isChecked) : ObservableObject
{
    public string Name { get; } = name;

    /// <summary>What the list shows: the pack and how many packages it holds.</summary>
    public string Label { get; } = name + " (" + packages + ")";

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

/// <summary>
/// "Scan For Models" in the XPLANE build (the old ScanForm_XPLANE): the X-Plane folder, the CSL folder derived from it,
/// whether to generate CSL objects (and liveries) and for which aircraft folders, the installed CSL packs to link, and the scan itself.
/// </summary>
public sealed partial class ScanXPlaneModelsViewModel : OverlayViewModel
{
    private readonly ProfileViewModel _profile;
    private readonly IXPlaneScanSource _source;
    private readonly IPlatform _platform;
    private readonly HashSet<string> _ticked;
    private readonly HashSet<string> _unlinked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Nothing was saved, so every aircraft folder is scanned: show it as such until the user ticks folders themselves.</summary>
    private bool _tickAll;

    public ScanXPlaneModelsViewModel(ProfileViewModel profile, IXPlaneScanSource source, IPlatform platform)
    {
        _profile = profile;
        _source = source;
        _platform = platform;
        _ticked = [.. source.InitialScanFolders];
        _tickAll = _ticked.Count == 0;
        Installs = source.Installs;
        _xplaneFolder = source.SimFolder;
        RefreshFolders();
        SelectedInstall = FindInstall(_xplaneFolder);
    }

    public override string Title => Loc.T("Scan for models");

    [ObservableProperty]
    private string _xplaneFolder;

    partial void OnXplaneFolderChanged(string value)
    {
        Status = "";
        SelectedInstall = FindInstall(value);
        RefreshFolders();
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
            XplaneFolder = value.Path;
    }

    private XPlaneInstallInfo? FindInstall(string folder) => Installs.FirstOrDefault(i => XPlaneFolderText.Same(i.Path, folder));

    /// <summary>Where the generated CSL objects go: read-only, always under the X-Plane folder.</summary>
    [ObservableProperty]
    private string _cslFolder = "";

    /// <summary>The aircraft folders found under the X-Plane folder. Shown only while <see cref="GenerateCsl"/> is on.</summary>
    public ObservableCollection<AircraftFolderViewModel> AircraftFolders { get; } = [];

    /// <summary>The CSL packs installed in X-Plane (X-CSL, Bluebell ...), linked into JoinFS's CSL folder unless switched off.</summary>
    public ObservableCollection<CslPackViewModel> CslPacks { get; } = [];

    public bool HasCslPacks => CslPacks.Count > 0;

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

    /// <summary>"Skip CSL objects already done" and the liveries only mean something while generating.</summary>
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

    public bool GenerateLiveries
    {
        get => _profile.GenerateLiveries;
        set
        {
            _profile.GenerateLiveries = value;
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

    /// <summary>
    /// Scans the folders ticked. All of them ticked is passed as "none", which the scan reads as every aircraft folder, so those installed
    /// later are scanned too. The options above are already saved as they change.
    /// </summary>
    [RelayCommand]
    private void Scan()
    {
        string folder = XplaneFolder.Trim();
        if (!_source.IsValidInstall(folder))
        {
            Status = XPlaneFolderText.NotAnInstall;
            return;
        }

        bool all = AircraftFolders.Count > 0 && AircraftFolders.All(f => f.IsChecked);
        IReadOnlyList<string> folders = all ? [] : [.. AircraftFolders.Where(f => f.IsChecked).Select(f => f.Name)];
        IReadOnlyList<string> unlinked = [.. CslPacks.Where(p => !p.IsChecked).Select(p => p.Name)];
        if (!_source.Scan(folder, folders, unlinked))
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
        foreach (CslPackViewModel pack in CslPacks)
        {
            if (pack.IsChecked)
                _unlinked.Remove(pack.Name);
            else
                _unlinked.Add(pack.Name);
        }

        CslFolder = string.IsNullOrWhiteSpace(XplaneFolder)
            ? ""
            : Path.Combine(XplaneFolder, "Resources", "plugins", "JoinFS", "Resources", "CSL");

        AircraftFolders.Clear();
        foreach (string name in _source.ListAircraftFolders(XplaneFolder))
            AircraftFolders.Add(new AircraftFolderViewModel(name, _tickAll || _ticked.Contains(name), () => _tickAll = false));

        CslPacks.Clear();
        foreach (CslPackInfo pack in _source.CslPacks(XplaneFolder))
        {
            // What the user switched off in this card wins over what was saved.
            bool linked = pack.Linked && !_unlinked.Contains(pack.Name);
            CslPacks.Add(new CslPackViewModel(pack.Name, pack.Packages, linked));
        }
        OnPropertyChanged(nameof(HasCslPacks));
    }
}
