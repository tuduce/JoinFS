using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>The steps of the first-run card, in the order they are asked.</summary>
public enum OnboardingStep
{
    /// <summary>The greeting, the nickname and, if the user wants, the SimBrief username.</summary>
    Welcome,

    /// <summary>The simulator folder, asked only when JoinFS did not find the simulator by itself.</summary>
    Simulator,

    /// <summary>The online hubs, to connect to one.</summary>
    Hubs,
}

/// <summary>One hub of the list the first-run card offers. Updated in place on each refresh, so the pick and the scroll position survive it.</summary>
public sealed partial class OnboardingHubRow : ObservableObject
{
    internal OnboardingHubRow(HubInfo hub) => Hub = hub;

    public HubInfo Hub { get; private set; }
    public string Id => Hub.Id;
    public string Name => Hub.Name;
    public string Status => Loc.T(Hub.Status.ToString());
    public int Users => Hub.Users;
    public int Aircraft => Hub.Aircraft;

    public bool IsOnline => Hub.Status == HubStatus.Online;
    public bool IsGlobal => Hub.Status == HubStatus.Global;
    public bool IsPassword => Hub.Status == HubStatus.Password;

    internal void Update(HubInfo hub)
    {
        Hub = hub;
        OnPropertyChanged(string.Empty);
    }
}

/// <summary>
/// First run only, a card over the expanded window that walks the new user through two steps: the nickname (required, and the SimBrief
/// username if they use it), then a list of the online hubs to connect to one. A third step, the simulator folder, comes between them only
/// when JoinFS did not find the simulator. Nothing is kept before the last step, so a user who quits half way is asked again.
/// <paramref name="finished"/> is called once the card has closed, with the hub that was picked (null when the user skipped the list);
/// the shell compacts the window then and connects what is to be connected. The scan for models starts as the card closes.
/// </summary>
public sealed partial class OnboardingViewModel : OverlayViewModel
{
    private readonly ProfileViewModel _profile;
    private readonly IModelScanSource _modelScan;
    private readonly IXPlaneScanSource _xplaneScan;
    private readonly IHubDirectory _hubDirectory;
    private readonly bool _isXPlaneBuild;
    private readonly Func<bool> _isSimulatorConnected;
    private readonly IPlatform _platform;
    private readonly Action<HubInfo?> _finished;
    private readonly string _knownFolder;
    private readonly Dictionary<string, OnboardingHubRow> _rowsById = [];
    private readonly SortController<OnboardingHubRow> _sort;
    private List<OnboardingHubRow> _joinable = [];

    public OnboardingViewModel(ProfileViewModel profile, IModelScanSource modelScan, IXPlaneScanSource xplaneScan, IHubDirectory hubDirectory,
        bool isXPlaneBuild, Func<bool> isSimulatorConnected, IPlatform platform, Action<HubInfo?> finished)
    {
        _profile = profile;
        _modelScan = modelScan;
        _xplaneScan = xplaneScan;
        _hubDirectory = hubDirectory;
        _isXPlaneBuild = isXPlaneBuild;
        _isSimulatorConnected = isSimulatorConnected;
        _platform = platform;
        _finished = finished;

        _knownFolder = (isXPlaneBuild ? xplaneScan.SimFolder : modelScan.SimFolder).Trim();
        _folder = _knownFolder;

        // The busiest hub first until the user sorts by another column; the counts sort from the highest, as in the Network Hubs tab.
        _sort = new SortController<OnboardingHubRow>(Rebuild, initialKey: "users");
        NameColumn = _sort.Add("name", Loc.T("Name"), r => r.Name);
        StatusColumn = _sort.Add("status", Loc.T("Status"), r => r.Status);
        UsersColumn = _sort.Add("users", Loc.T("Users"), r => r.Users, highestFirst: true);
        AircraftColumn = _sort.Add("aircraft", Loc.T("Aircraft"), r => r.Aircraft, highestFirst: true);
        Refresh();
    }

    public override string Title => Loc.T("Welcome to JoinFS");
    public override bool IsDismissable => false;

    // ---- where the user is

    /// <summary>The steps this user goes through: the folder is asked only when the simulator was not found.</summary>
    private IReadOnlyList<OnboardingStep> Steps =>
        _knownFolder.Length > 0
            ? [OnboardingStep.Welcome, OnboardingStep.Hubs]
            : [OnboardingStep.Welcome, OnboardingStep.Simulator, OnboardingStep.Hubs];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsSimulator), nameof(IsHubs), nameof(StepText), nameof(CanGoBack))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(ConnectCommand))]
    private OnboardingStep _step = OnboardingStep.Welcome;

    public bool IsWelcome => Step == OnboardingStep.Welcome;
    public bool IsSimulator => Step == OnboardingStep.Simulator;
    public bool IsHubs => Step == OnboardingStep.Hubs;

    public string StepText => Loc.F("Step {0} of {1}", Steps.ToList().IndexOf(Step) + 1, Steps.Count);

    // ---- 1. nickname and SimBrief

    /// <summary>The app wants at least this many characters; a shorter nickname used to be replaced by a generated one.</summary>
    public const int MinNicknameLength = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _nickname = "";

    /// <summary>True once the user has said they use SimBrief: the username is asked then, under the link that said it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoSimbrief))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _usesSimbrief;

    /// <summary>The link "Do you use SimBrief?" shows until it was clicked.</summary>
    public bool NoSimbrief => !UsesSimbrief;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _simbriefUsername = "";

    [RelayCommand]
    private void UseSimbrief() => UsesSimbrief = true;

    [RelayCommand]
    private void DropSimbrief() => UsesSimbrief = false;

    // ---- 2. the simulator folder, when it was not found

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderError), nameof(HasFolderError))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _folder;

    /// <summary>Which folder is meant, for the simulators where that is not obvious (the Packages folder of Microsoft Flight Simulator). Empty for X-Plane.</summary>
    public string FolderHint => _isXPlaneBuild ? "" : _modelScan.FolderPrompt;

    public bool HasFolderHint => FolderHint.Length > 0;

    /// <summary>Why the folder will not do, or null. Only X-Plane can tell a wrong folder; the other simulators take what they are given.</summary>
    public string? FolderError =>
        _isXPlaneBuild && Folder.Trim().Length > 0 && !_xplaneScan.IsValidInstall(Folder.Trim()) ? XPlaneFolderText.NotAnInstall : null;

    public bool HasFolderError => FolderError is not null;

    [RelayCommand]
    private async Task BrowseAsync()
    {
        string? picked = await _platform.PickFolderAsync(_isXPlaneBuild ? Loc.T("Select the root X-Plane folder") : Loc.T("Select the main simulator folder"));
        if (picked is not null)
            Folder = picked;
    }

    private bool CanScan => Folder.Trim().Length > 0 && FolderError is null;

    // ---- 3. the hubs

    /// <summary>The hubs that are online and can be joined, in the order of the column the user clicked (the busiest first to begin with).</summary>
    public ObservableCollection<OnboardingHubRow> Hubs { get; } = [];

    public SortColumn NameColumn { get; }
    public SortColumn StatusColumn { get; }
    public SortColumn UsersColumn { get; }
    public SortColumn AircraftColumn { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private OnboardingHubRow? _selectedHub;

    public bool HasHubs => Hubs.Count > 0;

    /// <summary>Reads the directory again, as the hubs answer. A hub that stays is updated in place and keeps the pick; one that went loses its row.</summary>
    public void Refresh()
    {
        List<OnboardingHubRow> wanted = [];
        HashSet<string> seen = [];
        IEnumerable<HubInfo> joinable = _hubDirectory.GetHubs()
            .Where(h => h.CanJoin && !h.Ignored && h.Status is HubStatus.Online or HubStatus.Global or HubStatus.Password)
            .OrderBy(h => h.Name, StringComparer.OrdinalIgnoreCase); // what the sort leaves in place when two hubs are equal
        foreach (HubInfo hub in joinable)
        {
            if (!seen.Add(hub.Id))
                continue;
            if (_rowsById.TryGetValue(hub.Id, out OnboardingHubRow? row))
                row.Update(hub);
            else
                _rowsById[hub.Id] = row = new OnboardingHubRow(hub);
            wanted.Add(row);
        }

        foreach (string gone in _rowsById.Keys.Except(seen).ToList())
            _rowsById.Remove(gone);

        _joinable = wanted;
        Rebuild();
    }

    private void Rebuild()
    {
        CollectionSync.Reconcile(Hubs, [.. _sort.Apply(_joinable)]);
        if (SelectedHub is not null && !Hubs.Contains(SelectedHub))
            SelectedHub = null;
        OnPropertyChanged(nameof(HasHubs));
    }

    private bool CanConnect => IsHubs && SelectedHub is not null;

    /// <summary>The Connect button and the double-click on a hub: finish the card and join the hub picked.</summary>
    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect() => Complete(SelectedHub?.Hub);

    /// <summary>Finish without joining a hub; the user joins one later from the strip or the Network Hubs tab.</summary>
    [RelayCommand]
    private void Skip() => Complete(null);

    // ---- moving on

    public bool CanGoBack => Step > OnboardingStep.Welcome;

    private bool CanNext => Step switch
    {
        OnboardingStep.Welcome => Nickname.Trim().Length >= MinNicknameLength && (!UsesSimbrief || !string.IsNullOrWhiteSpace(SimbriefUsername)),
        OnboardingStep.Simulator => FolderError is null,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() => Step = Steps[Steps.ToList().IndexOf(Step) + 1];

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step = Steps[Steps.ToList().IndexOf(Step) - 1];

    private void Complete(HubInfo? hub)
    {
        // A username typed and then given up again is not wanted.
        _profile.CompleteOnboarding(Nickname, UsesSimbrief ? SimbriefUsername : null);

        string folder = Folder.Trim();
        if (folder.Length > 0 && !XPlaneFolderText.Same(folder, _knownFolder))
        {
            if (_isXPlaneBuild)
                _xplaneScan.SaveFolder(folder);
            else
                _modelScan.SaveFolder(folder);
        }

        // The models are scanned without asking: without them the aircraft of the other pilots show as a default model.
        if (CanScan)
            StartScan(folder);

        Close();
        _finished(hub);
    }

    private void StartScan(string folder)
    {
        if (_isXPlaneBuild)
        {
            // every aircraft folder, and the CSL packs as they are linked now
            string[] unlinked = [.. _xplaneScan.CslPacks(folder).Where(p => !p.Linked).Select(p => p.Name)];
            _xplaneScan.Scan(folder, [], unlinked);
        }
        else if (_isSimulatorConnected())
        {
            string[] addOns = [.. _modelScan.AddOns.Where(a => a.Selected).Select(a => a.Key)];
            _modelScan.Scan(folder, _modelScan.ListsSubfolders ? _modelScan.InitialSubfolders : [], addOns, _modelScan.AdditionalFolders);
        }
        else
        {
            // The scan needs the simulator, so it is done as soon as it connects.
            _profile.ModelScanOnConnect = true;
        }
    }
}

/// <summary>Asks for a hub's password; <c>onConfirm</c> continues the join, <c>onCancel</c> hears that it was given up.</summary>
public sealed partial class PasswordPromptViewModel : OverlayViewModel
{
    private readonly Func<string, Task> _onConfirm;
    private readonly Action? _onCancel;
    private bool _confirmed;

    public PasswordPromptViewModel(string hubName, Func<string, Task> onConfirm, Action? onCancel = null)
    {
        HubName = hubName;
        _onConfirm = onConfirm;
        _onCancel = onCancel;
    }

    protected override void OnClosing()
    {
        if (!_confirmed)
            _onCancel?.Invoke();
    }

    public override string Title => Loc.T("Password Required");

    public string HubName { get; }
    public string Message => Loc.F("{0} requires a password to join.", HubName);

    [ObservableProperty]
    private string _password = "";

    [RelayCommand]
    private async Task ConfirmAsync()
    {
        _confirmed = true;
        Close();
        await _onConfirm(Password);
    }
}

/// <summary>Asks for the SimBrief username once, from Flight Plan. The shell stores it, then runs the import.</summary>
public sealed partial class SimbriefPromptViewModel : OverlayViewModel
{
    private readonly Func<string, Task> _onConfirm;

    public SimbriefPromptViewModel(Func<string, Task> onConfirm) => _onConfirm = onConfirm;

    public override string Title => Loc.T("Import from SimBrief");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    private string _username = "";

    private bool CanImport => !string.IsNullOrWhiteSpace(Username);

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        Close();
        await _onConfirm(Username.Trim());
    }
}
