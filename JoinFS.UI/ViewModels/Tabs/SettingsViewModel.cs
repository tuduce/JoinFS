using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>One accordion card. The parent keeps at most one open.</summary>
public abstract partial class SettingsSectionViewModel : ObservableObject
{
    private readonly Action<SettingsSectionViewModel> _toggle;

    protected SettingsSectionViewModel(string title, Action<SettingsSectionViewModel> toggle)
    {
        Title = title;
        _toggle = toggle;
    }

    public string Title { get; }

    [ObservableProperty]
    private bool _isOpen;

    [RelayCommand]
    private void Toggle() => _toggle(this);
}

public sealed partial class SimulatorSettingsViewModel : SettingsSectionViewModel
{
    private readonly IShell _shell;
    private readonly Func<bool> _isSimulatorConnected;
    private readonly IPlatform _platform;
    private readonly IXPlaneScanSource _xplaneScan;

    // Same bounds as the WinForms track bars.
    public const int CircleMinNm = 2, CircleMaxNm = 600;
    public const int FollowMinM = 20, FollowMaxM = 1000;

    internal SimulatorSettingsViewModel(Action<SettingsSectionViewModel> toggle, ProfileViewModel profile, IShell shell, Func<bool> isSimulatorConnected, IPlatform platform, IXPlaneScanSource xplaneScan, bool isXPlaneBuild)
        : base("Simulator", toggle)
    {
        _xplaneScan = xplaneScan;
        IsXPlaneBuild = isXPlaneBuild;
        Profile = profile;
        _shell = shell;
        _isSimulatorConnected = isSimulatorConnected;
        _platform = platform;
    }

    /// <summary>The floating-label options are for X-Plane only, so only the XPLANE build shows them.</summary>
    public bool IsXPlaneBuild { get; }

    /// <summary>Nickname and SimBrief username live in the profile, so Onboarding, Flight Plan and this card share them.</summary>
    public ProfileViewModel Profile { get; }

    [ObservableProperty] private bool _connectOnLaunch = true;
    [ObservableProperty] private bool _elevationCorrection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CircleLabel))]
    private int _circleOfActivityNm = 5;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowLabel))]
    private int _followDistanceM = 80;

    public string CircleLabel => $"Circle of activity: {CircleOfActivityNm} nm";
    public string FollowLabel => $"Follow distance: {FollowDistanceM} m";

    // The floating label JoinFS draws above other aircraft. X-Plane only.
    [ObservableProperty] private bool _showNickname = true;
    [ObservableProperty] private bool _showCallsign = true;
    [ObservableProperty] private bool _showDistance;
    [ObservableProperty] private bool _showAltitude;
    [ObservableProperty] private bool _showSpeed;

    /// <summary>"#RRGGBB" so the view model needs no UI types. The design's default is a warm yellow.</summary>
    [ObservableProperty] private string _labelColor = "#F2C400";

    /// <summary>Swatches offered by "Choose…". Any other colour can be typed as hex.</summary>
    public IReadOnlyList<string> LabelColorPresets { get; } =
        ["#F2C400", "#FFFFFF", "#FF5A4D", "#FF9F1C", "#7ED957", "#2EC4B6", "#3AA0FF", "#B388FF", "#FF7AB6", "#9AA4AF", "#222831"];

    [ObservableProperty]
    private bool _isLabelColorPickerOpen;

    [RelayCommand]
    private void ToggleLabelColorPicker() => IsLabelColorPickerOpen = !IsLabelColorPickerOpen;

    [RelayCommand]
    private void ChooseLabelColor(string color) => LabelColor = color;

    [ObservableProperty] private bool _autoImportSimbrief;

    [RelayCommand]
    private void OpenModelScanning() =>
        _shell.ShowOverlay(IsXPlaneBuild
            ? new ScanXPlaneModelsViewModel(Profile, _xplaneScan, _platform)
            : new ScanModelsViewModel(_isSimulatorConnected(), _platform));

    [RelayCommand]
    private void OpenModelMatching() => _shell.GoTo(TabId.Models);
}

public sealed partial class UserInterfaceSettingsViewModel(Action<SettingsSectionViewModel> toggle) : SettingsSectionViewModel("User Interface", toggle)
{
    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _autoRefresh = true;
    [ObservableProperty] private bool _toolTips = true;
}

public sealed partial class NetworkSettingsViewModel(Action<SettingsSectionViewModel> toggle) : SettingsSectionViewModel("Network", toggle)
{
    [ObservableProperty] private bool _chooseOwnPort;
    [ObservableProperty] private string _localPort = "";
    [ObservableProperty] private bool _joinGlobalAtLaunch = true;
    [ObservableProperty] private bool _lowBandwidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhazzupOptionsEnabled))]
    private bool _generateWhazzup = true;

    [ObservableProperty] private bool _whazzupIncludeGlobalUsers = true;
    [ObservableProperty] private bool _whazzupIncludeAi;

    /// <summary>The two "include" boxes only matter while WhazzUp is generated; they look disabled otherwise.</summary>
    public bool WhazzupOptionsEnabled => GenerateWhazzup;

    public bool PortEnabled => ChooseOwnPort;
    partial void OnChooseOwnPortChanged(bool value) => OnPropertyChanged(nameof(PortEnabled));

    [ObservableProperty] private string _password = "";
}

/// <summary>One of the five fields a user fills in to publish their own hub.</summary>
public sealed partial class HubFieldViewModel(string label, string placeholder) : ObservableObject
{
    public string Label { get; } = label;
    public string Placeholder { get; } = placeholder;

    [ObservableProperty]
    private string _value = "";
}

public sealed partial class HubModeSettingsViewModel : SettingsSectionViewModel
{
    internal HubModeSettingsViewModel(Action<SettingsSectionViewModel> toggle) : base("Hub Mode (Public)", toggle)
    {
        Fields =
        [
            new("Domain", "e.g. joinfs.example.com"),
            new("Name", "Public hub name"),
            new("About", "Short description"),
            new("Voice Server", "Voice server address"),
            new("Next Event", "Next scheduled event"),
        ];
    }

    [ObservableProperty]
    private bool _enabled;

    public IReadOnlyList<HubFieldViewModel> Fields { get; }
}

public sealed class AddressBookSettingsViewModel(Action<SettingsSectionViewModel> toggle, AddressBookViewModel book) : SettingsSectionViewModel("Address Book", toggle)
{
    public AddressBookViewModel Book { get; } = book;
}

public sealed partial class XPlaneSettingsViewModel : SettingsSectionViewModel
{
    /// <summary>What "Install C++…" opens: Microsoft's redistributable, which the X-Plane plugin needs.</summary>
    public const string CppRedistributableUrl = "https://aka.ms/vs/17/release/VC_redist.x64.exe";

    private readonly IShell _shell;
    private readonly IXPlanePluginInstaller _installer;
    private readonly IPlatform _platform;

    internal XPlaneSettingsViewModel(Action<SettingsSectionViewModel> toggle, IShell shell, IXPlanePluginInstaller installer, IPlatform platform)
        : base("X-Plane", toggle)
    {
        _shell = shell;
        _installer = installer;
        _platform = platform;
    }

    [ObservableProperty] private string _ipAddress = "";
    [ObservableProperty] private bool _tcas;

    [RelayCommand]
    private void InstallPlugin() => _shell.ShowOverlay(new InstallXPlanePluginViewModel(_installer, _platform));

    [RelayCommand]
    private void InstallCpp() => _platform.OpenUrl(CppRedistributableUrl);
}

public sealed partial class VariablesSettingsViewModel : SettingsSectionViewModel
{
    private readonly IShell _shell;
    private readonly IPlatform _platform;

    internal VariablesSettingsViewModel(Action<SettingsSectionViewModel> toggle, IVariablesCatalog catalog, IShell shell, IPlatform platform)
        : base("Variables", toggle)
    {
        _shell = shell;
        _platform = platform;
        foreach (VariableAssignment assignment in catalog.GetAssignments())
            Assignments.Add(new VariableAssignmentViewModel(assignment, Edit));
    }

    public ObservableCollection<VariableAssignmentViewModel> Assignments { get; } = [];

    private void Edit(VariableAssignmentViewModel row) =>
        _shell.ShowOverlay(new VariablesOverlayViewModel(row.Model, row.Files, _platform, files => row.Files = files));
}

public sealed partial class VariableAssignmentViewModel : ObservableObject
{
    private readonly Action<VariableAssignmentViewModel> _edit;

    internal VariableAssignmentViewModel(VariableAssignment assignment, Action<VariableAssignmentViewModel> edit)
    {
        Model = assignment.Model;
        _files = assignment.Files;
        _edit = edit;
    }

    public string Model { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilesText))]
    private IReadOnlyList<string> _files;

    public string FilesText => string.Join(", ", Files);

    [RelayCommand]
    private void Edit() => _edit(this);
}

/// <summary>Settings tab: an accordion of seven cards, all collapsed at first, at most one open.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    /// <param name="isXPlaneBuild">Only the XPLANE build has the X-Plane card.</param>
    public SettingsViewModel(ProfileViewModel profile, AddressBookViewModel addressBook, IVariablesCatalog variables, IXPlanePluginInstaller xplaneInstaller, IXPlaneScanSource xplaneScan,
        IShell shell, IPlatform platform, Func<bool> isSimulatorConnected, bool isXPlaneBuild)
    {
        Simulator = new SimulatorSettingsViewModel(Toggle, profile, shell, isSimulatorConnected, platform, xplaneScan, isXPlaneBuild);
        UserInterface = new UserInterfaceSettingsViewModel(Toggle);
        Network = new NetworkSettingsViewModel(Toggle);
        HubMode = new HubModeSettingsViewModel(Toggle);
        AddressBook = new AddressBookSettingsViewModel(Toggle, addressBook);
        XPlane = new XPlaneSettingsViewModel(Toggle, shell, xplaneInstaller, platform);
        IsXPlaneBuild = isXPlaneBuild;
        Variables = new VariablesSettingsViewModel(Toggle, variables, shell, platform);

        Sections = isXPlaneBuild
            ? [Simulator, UserInterface, Network, HubMode, AddressBook, XPlane, Variables]
            : [Simulator, UserInterface, Network, HubMode, AddressBook, Variables];
    }

    public SimulatorSettingsViewModel Simulator { get; }
    public UserInterfaceSettingsViewModel UserInterface { get; }
    public NetworkSettingsViewModel Network { get; }
    public HubModeSettingsViewModel HubMode { get; }
    public AddressBookSettingsViewModel AddressBook { get; }
    public XPlaneSettingsViewModel XPlane { get; }
    public VariablesSettingsViewModel Variables { get; }

    public bool IsXPlaneBuild { get; }

    /// <summary>The cards shown. The X-Plane card is only there in the XPLANE build.</summary>
    public IReadOnlyList<SettingsSectionViewModel> Sections { get; }

    /// <summary>The open section, or null. Clicking the open card's header closes it; clicking another opens that one.</summary>
    public SettingsSectionViewModel? OpenSection => Sections.FirstOrDefault(s => s.IsOpen);

    private void Toggle(SettingsSectionViewModel section)
    {
        bool open = !section.IsOpen;
        foreach (SettingsSectionViewModel other in Sections)
            other.IsOpen = false;
        section.IsOpen = open;
        OnPropertyChanged(nameof(OpenSection));
    }
}
