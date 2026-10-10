using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>One line of a list of things the scan can include, with its tick: a subfolder or an add-on.</summary>
public sealed partial class ScanChoiceViewModel(string key, string name, bool isChecked) : ObservableObject
{
    /// <summary>What the scan knows it by.</summary>
    public string Key { get; } = key;

    /// <summary>What is shown.</summary>
    public string Name { get; } = name;

    [ObservableProperty]
    private bool _isChecked = isChecked;
}

/// <summary>
/// "Scan For Models", opened from Settings → Simulator (every build but XPLANE, which has <see cref="ScanXPlaneModelsViewModel"/>): where the
/// simulator keeps its models, which of them to include, and the scan itself. The choice is remembered for next time.
/// </summary>
public sealed partial class ScanModelsViewModel : OverlayViewModel
{
    private readonly IModelScanSource _source;
    private readonly IPlatform _platform;
    private readonly HashSet<string> _ticked;

    public ScanModelsViewModel(IModelScanSource source, bool simulatorConnected, IPlatform platform)
    {
        _source = source;
        _platform = platform;
        SimulatorConnected = simulatorConnected;

        Subfolders.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoSubfolders));
        AddOns.CollectionChanged += (_, _) => OnPropertyChanged(nameof(NoAddOns));

        _ticked = [.. source.InitialSubfolders];
        _rootFolder = source.SimFolder;
        foreach (ScanAddOn addOn in source.AddOns)
            AddOns.Add(new ScanChoiceViewModel(addOn.Key, addOn.Name, addOn.Selected));
        _otherFoldersText = string.Join(Environment.NewLine, source.AdditionalFolders);
        RefreshSubfolders();
    }

    public override string Title => Loc.T("Scan for models");

    public string SimulatorName => _source.SimulatorName;
    public bool SimulatorConnected { get; }
    public string SimulatorLabel => SimulatorConnected ? Loc.T("Connected") : Loc.T("Not connected");

    /// <summary>What the folder is asked for; it differs from one simulator to another.</summary>
    public string FolderPrompt => _source.FolderPrompt;

    [ObservableProperty]
    private string _rootFolder;

    partial void OnRootFolderChanged(string value) => RefreshSubfolders();

    /// <summary>Only a simulator with one SimObjects folder has subfolders to choose; the others nest them by package.</summary>
    public bool ShowSubfolders => _source.ListsSubfolders;

    public ObservableCollection<ScanChoiceViewModel> Subfolders { get; } = [];
    public ObservableCollection<ScanChoiceViewModel> AddOns { get; } = [];

    // What the two lists say when they are empty.
    public bool NoSubfolders => Subfolders.Count == 0;
    public bool NoAddOns => AddOns.Count == 0;

    /// <summary>More folders to scan, one to a line.</summary>
    [ObservableProperty]
    private string _otherFoldersText;

    /// <summary>Why the scan did not start, when it needs saying. Empty otherwise.</summary>
    [ObservableProperty]
    private string _status = "";

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? folder = await _platform.PickFolderAsync(Loc.T("Select the main simulator folder"));
        if (folder is not null)
            RootFolder = folder;
    }

    /// <summary>A scan needs the simulator: the models are listed by it.</summary>
    private bool CanScan => SimulatorConnected;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void Scan()
    {
        IReadOnlyList<string> subfolders = ShowSubfolders ? [.. Subfolders.Where(f => f.IsChecked).Select(f => f.Key)] : [];
        IReadOnlyList<string> addOns = [.. AddOns.Where(a => a.IsChecked).Select(a => a.Key)];
        IReadOnlyList<string> others = [.. OtherFoldersText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];

        if (!_source.Scan(RootFolder.Trim(), subfolders, addOns, others))
        {
            Status = Loc.T("A scan is already running.");
            return;
        }
        Close();
    }

    private void RefreshSubfolders()
    {
        // Remember the ticks across a folder change, as the old dialog did.
        foreach (ScanChoiceViewModel folder in Subfolders)
        {
            if (folder.IsChecked)
                _ticked.Add(folder.Key);
            else
                _ticked.Remove(folder.Key);
        }

        Subfolders.Clear();
        if (!ShowSubfolders)
            return;
        foreach (string name in _source.ListSubfolders(RootFolder))
            Subfolders.Add(new ScanChoiceViewModel(name, name, _ticked.Contains(name)));
    }
}
