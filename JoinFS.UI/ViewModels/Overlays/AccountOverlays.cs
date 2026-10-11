using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>The steps of the first-run card, in the order they are asked.</summary>
public enum OnboardingStep
{
    /// <summary>The greeting and the nickname.</summary>
    Welcome,

    /// <summary>Whether the user uses SimBrief, and their username.</summary>
    SimBrief,

    /// <summary>The simulator folder JoinFS knows, which the user may change.</summary>
    Simulator,

    /// <summary>Whether to scan for models now.</summary>
    Models,
}

/// <summary>
/// First run only, a card over the expanded window that walks the new user through four steps: the nickname (required), SimBrief (optional),
/// the simulator folder, and whether to scan for models. Nothing is kept before the last step, so a user who quits half way is asked again.
/// <paramref name="finished"/> is called once the card has closed; the shell compacts the window then.
/// </summary>
public sealed partial class OnboardingViewModel : OverlayViewModel
{
    private const int StepCount = 4;

    private readonly ProfileViewModel _profile;
    private readonly IModelScanSource _modelScan;
    private readonly IXPlaneScanSource _xplaneScan;
    private readonly bool _isXPlaneBuild;
    private readonly Func<bool> _isSimulatorConnected;
    private readonly IPlatform _platform;
    private readonly Action _finished;
    private readonly string _knownFolder;

    public OnboardingViewModel(ProfileViewModel profile, IModelScanSource modelScan, IXPlaneScanSource xplaneScan, bool isXPlaneBuild,
        Func<bool> isSimulatorConnected, IPlatform platform, Action finished)
    {
        _profile = profile;
        _modelScan = modelScan;
        _xplaneScan = xplaneScan;
        _isXPlaneBuild = isXPlaneBuild;
        _isSimulatorConnected = isSimulatorConnected;
        _platform = platform;
        _finished = finished;

        _knownFolder = (isXPlaneBuild ? xplaneScan.SimFolder : modelScan.SimFolder).Trim();
        _folder = _knownFolder;
    }

    public override string Title => Loc.T("Welcome to JoinFS");
    public override bool IsDismissable => false;

    // ---- where the user is

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWelcome), nameof(IsSimbrief), nameof(IsSimulator), nameof(IsModels), nameof(StepText), nameof(CanGoBack),
        nameof(ScanWaitsForSimulator), nameof(CanScan), nameof(HasNoFolderToScan))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(ScanCommand))]
    private OnboardingStep _step = OnboardingStep.Welcome;

    public bool IsWelcome => Step == OnboardingStep.Welcome;
    public bool IsSimbrief => Step == OnboardingStep.SimBrief;
    public bool IsSimulator => Step == OnboardingStep.Simulator;
    public bool IsModels => Step == OnboardingStep.Models;

    public string StepText => Loc.F("Step {0} of {1}", (int)Step + 1, StepCount);

    // ---- 1. nickname

    /// <summary>The app wants at least this many characters; a shorter nickname used to be replaced by a generated one.</summary>
    public const int MinNicknameLength = 2;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _nickname = "";

    // ---- 2. SimBrief

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NoSimbrief))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _usesSimbrief;

    /// <summary>The other radio button of the question: it is checked exactly when <see cref="UsesSimbrief"/> is not.</summary>
    public bool NoSimbrief
    {
        get => !UsesSimbrief;
        set => UsesSimbrief = !value;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private string _simbriefUsername = "";

    // ---- 3. the simulator folder

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FolderError), nameof(HasFolderError), nameof(CanScan), nameof(HasNoFolderToScan))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(ScanCommand))]
    private string _folder;

    /// <summary>What is said above the folder: where JoinFS looked, or that it found nothing.</summary>
    public string FolderIntro => _knownFolder.Length > 0
        ? Loc.T("JoinFS found your simulator in this folder. Change it if that is not right.")
        : Loc.T("JoinFS did not find your simulator. Please choose its folder.");

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

    // ---- 4. scan for models

    /// <summary>The models are listed by the simulator, so with SimConnect the scan has to wait until it is connected.</summary>
    public bool ScanWaitsForSimulator => !_isXPlaneBuild && !_isSimulatorConnected();

    public bool CanScan => Folder.Trim().Length > 0 && FolderError is null;

    public bool HasNoFolderToScan => Folder.Trim().Length == 0;

    // ---- moving on

    public bool CanGoBack => Step > OnboardingStep.Welcome;

    private bool CanNext => Step switch
    {
        OnboardingStep.Welcome => Nickname.Trim().Length >= MinNicknameLength,
        OnboardingStep.SimBrief => !UsesSimbrief || !string.IsNullOrWhiteSpace(SimbriefUsername),
        OnboardingStep.Simulator => FolderError is null,
        _ => false,
    };

    [RelayCommand(CanExecute = nameof(CanNext))]
    private void Next() => Step++;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => Step--;

    [RelayCommand(CanExecute = nameof(CanScan))]
    private void Scan() => Complete(scan: true);

    [RelayCommand]
    private void SkipScan() => Complete(scan: false);

    private void Complete(bool scan)
    {
        // A username typed and then switched off again is not wanted.
        _profile.CompleteOnboarding(Nickname, UsesSimbrief ? SimbriefUsername : null);

        string folder = Folder.Trim();
        if (folder.Length > 0 && !XPlaneFolderText.Same(folder, _knownFolder))
        {
            if (_isXPlaneBuild)
                _xplaneScan.SaveFolder(folder);
            else
                _modelScan.SaveFolder(folder);
        }

        if (scan && CanScan)
            StartScan(folder);

        Close();
        _finished();
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
