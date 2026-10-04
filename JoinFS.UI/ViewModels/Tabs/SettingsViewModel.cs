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

/// <summary>
/// The Preferences being edited, shared by the cards, and the store they are saved to. A card saves as soon as one of its
/// settings changes, so nothing is lost by closing the window.
/// </summary>
internal sealed class PreferencesSession(IPreferencesStore store)
{
    public Preferences Current { get; } = store.Load();

    public void Commit() => store.Save(Current.Clone());
}

/// <summary>A card whose settings are Preferences: any change of one of them is written to the shared session and saved.</summary>
public abstract class PersistedSettingsSectionViewModel : SettingsSectionViewModel
{
    private readonly PreferencesSession _preferences;

    private protected PersistedSettingsSectionViewModel(string title, Action<SettingsSectionViewModel> toggle, PreferencesSession preferences)
        : base(title, toggle) => _preferences = preferences;

    /// <summary>The Preferences as they are now. Cards read their starting values from it.</summary>
    private protected Preferences Prefs => _preferences.Current;

    /// <summary>True for a property that is one of the card's settings, not for a label or a flag that only drives the view.</summary>
    private protected abstract bool Persists(string? property);

    /// <summary>Copies the card's settings to <paramref name="into"/>. False when they cannot be saved as they are, so nothing is saved.</summary>
    private protected abstract bool Write(Preferences into);

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (Persists(e.PropertyName) && Write(_preferences.Current))
            _preferences.Commit();
    }
}

public sealed partial class SimulatorSettingsViewModel : PersistedSettingsSectionViewModel
{
    private readonly IShell _shell;
    private readonly Func<bool> _isSimulatorConnected;
    private readonly IPlatform _platform;
    private readonly IXPlaneScanSource _xplaneScan;

    // Same bounds as the WinForms track bars.
    public const int CircleMinNm = 2, CircleMaxNm = 600;
    public const int FollowMinM = 20, FollowMaxM = 1000;

    internal SimulatorSettingsViewModel(Action<SettingsSectionViewModel> toggle, PreferencesSession preferences, ProfileViewModel profile, IShell shell, Func<bool> isSimulatorConnected, IPlatform platform, IXPlaneScanSource xplaneScan, bool isXPlaneBuild)
        : base("Simulator", toggle, preferences)
    {
        _xplaneScan = xplaneScan;
        IsXPlaneBuild = isXPlaneBuild;
        Profile = profile;
        _shell = shell;
        _isSimulatorConnected = isSimulatorConnected;
        _platform = platform;

        Preferences p = Prefs;
        _connectOnLaunch = p.ConnectOnLaunch;
        _elevationCorrection = p.ElevationCorrection;
        _circleOfActivityNm = Math.Clamp(p.CircleOfActivityNm, CircleMinNm, CircleMaxNm);
        _followDistanceM = Math.Clamp(p.FollowDistanceM, FollowMinM, FollowMaxM);
        _showNickname = p.ShowNickname;
        _showCallsign = p.ShowCallsign;
        _showDistance = p.ShowDistance;
        _showAltitude = p.ShowAltitude;
        _showSpeed = p.ShowSpeed;
        _labelColor = p.LabelColor;
        _autoImportSimbrief = p.AutoImportSimbrief;
    }

    private protected override bool Persists(string? property) =>
        property is nameof(ConnectOnLaunch) or nameof(ElevationCorrection) or nameof(CircleOfActivityNm) or nameof(FollowDistanceM)
            or nameof(ShowNickname) or nameof(ShowCallsign) or nameof(ShowDistance) or nameof(ShowAltitude) or nameof(ShowSpeed)
            or nameof(LabelColor) or nameof(AutoImportSimbrief);

    private protected override bool Write(Preferences p)
    {
        p.ConnectOnLaunch = ConnectOnLaunch;
        p.ElevationCorrection = ElevationCorrection;
        p.CircleOfActivityNm = CircleOfActivityNm;
        p.FollowDistanceM = FollowDistanceM;
        p.ShowNickname = ShowNickname;
        p.ShowCallsign = ShowCallsign;
        p.ShowDistance = ShowDistance;
        p.ShowAltitude = ShowAltitude;
        p.ShowSpeed = ShowSpeed;
        p.AutoImportSimbrief = AutoImportSimbrief;

        // A colour typed by hand is saved once it is a whole #RRGGBB.
        if (IsColor(LabelColor))
            p.LabelColor = LabelColor.ToUpperInvariant();
        return true;
    }

    private static bool IsColor(string text) =>
        text is { Length: 7 } && text[0] == '#' && text.Skip(1).All(Uri.IsHexDigit);

    /// <summary>The floating-label options are for X-Plane only, so only the XPLANE build shows them.</summary>
    public bool IsXPlaneBuild { get; }

    /// <summary>Nickname and SimBrief username live in the profile, so Onboarding, Flight Plan and this card share them.</summary>
    public ProfileViewModel Profile { get; }

    [ObservableProperty] private bool _connectOnLaunch;
    [ObservableProperty] private bool _elevationCorrection;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CircleLabel))]
    private int _circleOfActivityNm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FollowLabel))]
    private int _followDistanceM;

    public string CircleLabel => $"Circle of activity: {CircleOfActivityNm} nm";
    public string FollowLabel => $"Follow distance: {FollowDistanceM} m";

    // The floating label JoinFS draws above other aircraft. X-Plane only.
    [ObservableProperty] private bool _showNickname;
    [ObservableProperty] private bool _showCallsign;
    [ObservableProperty] private bool _showDistance;
    [ObservableProperty] private bool _showAltitude;
    [ObservableProperty] private bool _showSpeed;

    /// <summary>"#RRGGBB" so the view model needs no UI types. The design's default is a warm yellow.</summary>
    [ObservableProperty] private string _labelColor;

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

public sealed partial class UserInterfaceSettingsViewModel : PersistedSettingsSectionViewModel
{
    internal UserInterfaceSettingsViewModel(Action<SettingsSectionViewModel> toggle, PreferencesSession preferences)
        : base("User Interface", toggle, preferences)
    {
        _alwaysOnTop = Prefs.AlwaysOnTop;
        _autoRefresh = Prefs.AutoRefresh;
        _toolTips = Prefs.ToolTips;
    }

    [ObservableProperty] private bool _alwaysOnTop;
    [ObservableProperty] private bool _autoRefresh;
    [ObservableProperty] private bool _toolTips;

    private protected override bool Persists(string? property) =>
        property is nameof(AlwaysOnTop) or nameof(AutoRefresh) or nameof(ToolTips);

    private protected override bool Write(Preferences p)
    {
        p.AlwaysOnTop = AlwaysOnTop;
        p.AutoRefresh = AutoRefresh;
        p.ToolTips = ToolTips;
        return true;
    }
}

public sealed partial class NetworkSettingsViewModel : PersistedSettingsSectionViewModel
{
    internal NetworkSettingsViewModel(Action<SettingsSectionViewModel> toggle, PreferencesSession preferences)
        : base("Network", toggle, preferences)
    {
        Preferences p = Prefs;
        _chooseOwnPort = p.ChooseOwnPort;
        _localPort = p.LocalPort.ToString();
        _joinGlobalAtLaunch = p.JoinGlobalAtLaunch;
        _lowBandwidth = p.LowBandwidth;
        _generateWhazzup = p.GenerateWhazzup;
        _whazzupIncludeGlobalUsers = p.WhazzupIncludeGlobalUsers;
        _whazzupIncludeAi = p.WhazzupIncludeAi;
        _password = p.Password;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortEnabled), nameof(PortError))]
    private bool _chooseOwnPort;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PortError))]
    private string _localPort;

    [ObservableProperty] private bool _joinGlobalAtLaunch;
    [ObservableProperty] private bool _lowBandwidth;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WhazzupOptionsEnabled))]
    private bool _generateWhazzup;

    [ObservableProperty] private bool _whazzupIncludeGlobalUsers;
    [ObservableProperty] private bool _whazzupIncludeAi;

    /// <summary>The two "include" boxes only matter while WhazzUp is generated; they look disabled otherwise.</summary>
    public bool WhazzupOptionsEnabled => GenerateWhazzup;

    public bool PortEnabled => ChooseOwnPort;

    /// <summary>The port to use as typed, or null when it is not a port.</summary>
    private int? ParsedPort =>
        int.TryParse(LocalPort.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int port) && port is >= 1 and <= 65535 ? port : null;

    /// <summary>Shown while the port is chosen and what is typed is not a port; the port in use stays as it was.</summary>
    public string? PortError => ChooseOwnPort && ParsedPort is null ? "Enter a port from 1 to 65535." : null;

    [ObservableProperty] private string _password;

    private protected override bool Persists(string? property) =>
        property is nameof(ChooseOwnPort) or nameof(LocalPort) or nameof(JoinGlobalAtLaunch) or nameof(LowBandwidth) or nameof(GenerateWhazzup)
            or nameof(WhazzupIncludeGlobalUsers) or nameof(WhazzupIncludeAi) or nameof(Password);

    private protected override bool Write(Preferences p)
    {
        p.JoinGlobalAtLaunch = JoinGlobalAtLaunch;
        p.LowBandwidth = LowBandwidth;
        p.GenerateWhazzup = GenerateWhazzup;
        p.WhazzupIncludeGlobalUsers = WhazzupIncludeGlobalUsers;
        p.WhazzupIncludeAi = WhazzupIncludeAi;
        p.Password = Password.Trim();

        // Opening a port is not undone by typing on: only a whole, valid number replaces the port in use.
        p.ChooseOwnPort = ChooseOwnPort;
        if (ParsedPort is int port)
            p.LocalPort = port;
        return true;
    }
}

/// <summary>One of the five fields a user fills in to publish their own hub. <paramref name="changed"/> is told when the value changes.</summary>
public sealed partial class HubFieldViewModel(string label, string placeholder, Action? changed = null) : ObservableObject
{
    public string Label { get; } = label;
    public string Placeholder { get; } = placeholder;

    [ObservableProperty]
    private string _value = "";

    partial void OnValueChanged(string value) => changed?.Invoke();
}

public sealed partial class HubModeSettingsViewModel : PersistedSettingsSectionViewModel
{
    /// <summary>A public hub needs a name of at least this many characters, as in the WinForms dialog.</summary>
    public const int MinNameLength = 3;

    private readonly HubFieldViewModel _domain, _name, _about, _voice, _nextEvent;
    private bool _loading = true;

    internal HubModeSettingsViewModel(Action<SettingsSectionViewModel> toggle, PreferencesSession preferences)
        : base("Hub Mode (Public)", toggle, preferences)
    {
        // Typing in a field is a change of the card, so the fields call back into it.
        _domain = new("Domain", "e.g. joinfs.example.com", FieldChanged);
        _name = new("Name", "Public hub name", FieldChanged);
        _about = new("About", "Short description", FieldChanged);
        _voice = new("Voice Server", "Voice server address", FieldChanged);
        _nextEvent = new("Next Event", "Next scheduled event", FieldChanged);
        Fields = [_domain, _name, _about, _voice, _nextEvent];

        Preferences p = Prefs;
        _enabled = p.HubMode;
        _domain.Value = p.HubDomain;
        _name.Value = p.HubName;
        _about.Value = p.HubAbout;
        _voice.Value = p.HubVoice;
        _nextEvent.Value = p.HubEvent;
        _loading = false;
    }

    [ObservableProperty]
    private bool _enabled;

    public IReadOnlyList<HubFieldViewModel> Fields { get; }

    /// <summary>Shown while hub mode is on and the name is too short. Nothing of the card is saved until the name is fixed.</summary>
    [ObservableProperty]
    private string? _nameError;

    private void FieldChanged()
    {
        if (!_loading)
            OnPropertyChanged(nameof(Fields));
    }

    private protected override bool Persists(string? property) =>
        property is nameof(Enabled) or nameof(Fields);

    private protected override bool Write(Preferences p)
    {
        if (Enabled && _name.Value.Trim().Length < MinNameLength)
        {
            NameError = $"The hub name must be at least {MinNameLength} characters long.";
            return false;
        }

        NameError = null;
        p.HubMode = Enabled;
        p.HubDomain = _domain.Value.Trim();
        p.HubName = _name.Value.Trim();
        p.HubAbout = _about.Value.Trim();
        p.HubVoice = _voice.Value.Trim();
        p.HubEvent = _nextEvent.Value.Trim();
        return true;
    }
}

public sealed class AddressBookSettingsViewModel(Action<SettingsSectionViewModel> toggle, AddressBookViewModel book) : SettingsSectionViewModel("Address Book", toggle)
{
    public AddressBookViewModel Book { get; } = book;
}

public sealed partial class XPlaneSettingsViewModel : PersistedSettingsSectionViewModel
{
    /// <summary>What "Install C++…" opens: Microsoft's redistributable, which the X-Plane plugin needs.</summary>
    public const string CppRedistributableUrl = "https://aka.ms/vs/17/release/VC_redist.x64.exe";

    private readonly IShell _shell;
    private readonly IXPlanePluginInstaller _installer;
    private readonly IPlatform _platform;

    internal XPlaneSettingsViewModel(Action<SettingsSectionViewModel> toggle, PreferencesSession preferences, IShell shell, IXPlanePluginInstaller installer, IPlatform platform)
        : base("X-Plane", toggle, preferences)
    {
        _shell = shell;
        _installer = installer;
        _platform = platform;
        _ipAddress = Prefs.XPlaneAddress;
        _tcas = Prefs.Tcas;
    }

    [ObservableProperty] private string _ipAddress;
    [ObservableProperty] private bool _tcas;

    private protected override bool Persists(string? property) => property is nameof(IpAddress) or nameof(Tcas);

    private protected override bool Write(Preferences p)
    {
        p.XPlaneAddress = IpAddress.Trim();
        p.Tcas = Tcas;
        return true;
    }

    [RelayCommand]
    private void InstallPlugin() => _shell.ShowOverlay(new InstallXPlanePluginViewModel(_installer, _platform));

    [RelayCommand]
    private void InstallCpp() => _platform.OpenUrl(CppRedistributableUrl);
}

public sealed partial class VariablesSettingsViewModel : SettingsSectionViewModel
{
    private readonly IVariablesCatalog _catalog;
    private readonly IModelCatalog _models;
    private readonly IShell _shell;
    private readonly IPlatform _platform;
    private readonly Dictionary<string, VariableAssignmentViewModel> _rowsByModel = [];

    internal VariablesSettingsViewModel(Action<SettingsSectionViewModel> toggle, IVariablesCatalog catalog, IModelCatalog models, IShell shell, IPlatform platform)
        : base("Variables", toggle)
    {
        _catalog = catalog;
        _models = models;
        _shell = shell;
        _platform = platform;
        Refresh();

        // Opening the card reads the lists again, so it opens on what is true now.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsOpen) && IsOpen)
                Refresh();
        };
    }

    public ObservableCollection<VariableAssignmentViewModel> Assignments { get; } = [];

    /// <summary>Reads the models that have variable files of their own. Rows still there are updated in place.</summary>
    public void Refresh()
    {
        List<VariableAssignmentViewModel> wanted = [];
        HashSet<string> seen = [];
        foreach (VariableAssignment assignment in _catalog.GetAssignments())
        {
            if (!seen.Add(assignment.Model))
                continue;
            if (_rowsByModel.TryGetValue(assignment.Model, out VariableAssignmentViewModel? row))
                row.Files = assignment.Files;
            else
                _rowsByModel[assignment.Model] = row = new VariableAssignmentViewModel(assignment, Edit);
            wanted.Add(row);
        }

        foreach (string gone in _rowsByModel.Keys.Except(seen).ToList())
            _rowsByModel.Remove(gone);

        CollectionSync.Reconcile(Assignments, wanted);
    }

    private void Edit(VariableAssignmentViewModel row)
    {
        VariablesOverlayViewModel overlay = new(row.Model, _catalog, _models, _platform);
        // What the overlay changed shows in the list as soon as it is closed.
        overlay.CloseRequested += (_, _) => Refresh();
        _shell.ShowOverlay(overlay);
    }
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
    public SettingsViewModel(ProfileViewModel profile, AddressBookViewModel addressBook, IPreferencesStore preferences, IVariablesCatalog variables, IModelCatalog models, IXPlanePluginInstaller xplaneInstaller, IXPlaneScanSource xplaneScan,
        IShell shell, IPlatform platform, Func<bool> isSimulatorConnected, bool isXPlaneBuild)
    {
        PreferencesSession session = new(preferences);
        Simulator = new SimulatorSettingsViewModel(Toggle, session, profile, shell, isSimulatorConnected, platform, xplaneScan, isXPlaneBuild);
        UserInterface = new UserInterfaceSettingsViewModel(Toggle, session);
        Network = new NetworkSettingsViewModel(Toggle, session);
        HubMode = new HubModeSettingsViewModel(Toggle, session);
        AddressBook = new AddressBookSettingsViewModel(Toggle, addressBook);
        XPlane = new XPlaneSettingsViewModel(Toggle, session, shell, xplaneInstaller, platform);
        IsXPlaneBuild = isXPlaneBuild;
        Variables = new VariablesSettingsViewModel(Toggle, variables, models, shell, platform);

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
