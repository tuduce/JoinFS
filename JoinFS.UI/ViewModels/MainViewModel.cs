using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.ViewModels;

/// <summary>
/// The app shell: collapsed or expanded, the active tab, the overlay on top, and the three connectors.
/// Tabs reach it only through <see cref="IShell"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IShell
{
    private readonly AppServices _services;

    // The next Network connect, set by whoever asks for it (Join, hub row, Create mesh).
    private Func<CancellationToken, Task> _networkAction = _ => Task.CompletedTask;

    public MainViewModel(AppServices services)
    {
        _services = services;

        Profile = new ProfileViewModel(services.Settings);
        AddressBook = new AddressBookViewModel(services.AddressBook);

        Simulator = new ConnectionViewModel(ConnectionLabels.Simulator, services.Simulator.ConnectAsync, services.Simulator.DisconnectAsync,
            observed: services.Simulator.ReportsState);
        Network = new ConnectionViewModel(ConnectionLabels.Network, ct => _networkAction(ct), services.Network.DisconnectAsync,
            requestConnect: JoinSelectedAsync, observed: services.Network.ReportsState);

        Home = new HomeViewModel(this, services.Session, services.Traffic, services.App, services.Platform, services.MapTiles);
        Hubs = new HubsViewModel(services.Hubs, services.Network, this);
        Session = new SessionViewModel(services.Session);
        RecordSelection = new RecordSelection();
        Aircraft = new AircraftViewModel(services.Traffic, services.Models, services.Variables, services.Platform, Profile, RecordSelection, this);
        Objects = new ObjectsViewModel(services.Traffic, services.Models, Profile, this);
        ModelMatching = new ModelMatchingViewModel(services.Models, this);
        FlightPlan = new FlightPlanViewModel(services.FlightPlan, services.SimBrief, Profile, this);
        Recorder = new RecorderViewModel(services.Recorder, services.Traffic, RecordSelection, services.Platform, this);
        Chat = new ChatViewModel(services.Chat);
        Monitor = new MonitorViewModel(services.Monitor, services.Platform);
        Settings = new SettingsViewModel(Profile, AddressBook, services.Preferences, services.Variables, services.Models, services.XPlanePlugin, services.XPlaneScan, services.ModelScan, this, services.Platform, () => Simulator.IsConnected, services.App.IsXPlaneBuild);

        // The strip's flight-plan button fetches from SimBrief. If a username is still needed the prompt comes first and
        // the import finishes after it, so this attempt ends "not loaded" and the import itself reports back through Imported.
        // That also covers an import started from the Flight Plan tab. The button makes what it fetches the user's plan at once, as the
        // old SimBrief button did; the tab's own link only shows it. Clicking "Loaded" forgets the fetch, the plan stays.
        FlightPlanLoad = new ConnectionViewModel(ConnectionLabels.FlightPlan, async _ =>
            {
                if (!await FlightPlan.TryImportFromSimbriefAsync(commit: true))
                    throw new OperationCanceledException();
            },
            disconnect: () =>
            {
                services.SimBrief.Reset();
                return Task.CompletedTask;
            },
            observed: services.SimBrief.ReportsState);
        FlightPlan.Imported += (_, _) => FlightPlanLoad.SetState(ConnectionState.Connected);
        // A plan saved from the tab is loaded too, whatever it came from; a cleared one is not.
        FlightPlan.Saved += (_, filled) => FlightPlanLoad.SetState(filled ? ConnectionState.Connected : ConnectionState.Disconnected);

        // Save in the Session tab changes the address book; the strip's hub picker has to show it.
        Session.AddressBookChanged += (_, _) => AddressBook.Reload();
        Hubs.AddressBookChanged += (_, _) => AddressBook.Reload();

        NavItems =
        [
            new(this, TabId.Home, "Home", "IconHome"),
            new(this, TabId.Network, "Network Hubs", "IconHubs"),
            new(this, TabId.Session, "Session", "IconSession"),
            new(this, TabId.Aircraft, "Aircraft", "IconAircraft"),
            new(this, TabId.Objects, "Objects", "IconObjects"),
            new(this, TabId.Models, "Model Matching", "IconModels"),
            new(this, TabId.FlightPlan, "Flight Plan", "IconFlightPlan"),
            new(this, TabId.Recorder, "Recorder", "IconRecorder"),
            new(this, TabId.Chat, "Chat", "IconChat"),
            new(this, TabId.Monitor, "Monitor", "IconMonitor"),
            new(this, TabId.Settings, "Settings", "IconSettings"),
        ];

        _hasNewChat = services.Chat.HasUnread;
        RefreshNav();

        if (!Profile.Onboarded)
            ShowOverlay(new OnboardingViewModel(Profile));
    }

    public ProfileViewModel Profile { get; }
    public AddressBookViewModel AddressBook { get; }

    /// <summary>Which aircraft are recorded. Shown by the Aircraft tab and the Recorder tab.</summary>
    public RecordSelection RecordSelection { get; }

    public ConnectionViewModel Simulator { get; }
    public ConnectionViewModel Network { get; }
    public ConnectionViewModel FlightPlanLoad { get; }

    public HomeViewModel Home { get; }
    public HubsViewModel Hubs { get; }
    public SessionViewModel Session { get; }
    public AircraftViewModel Aircraft { get; }
    public ObjectsViewModel Objects { get; }
    public ModelMatchingViewModel ModelMatching { get; }
    public FlightPlanViewModel FlightPlan { get; }
    public RecorderViewModel Recorder { get; }
    public ChatViewModel Chat { get; }
    public MonitorViewModel Monitor { get; }
    public SettingsViewModel Settings { get; }

    public IReadOnlyList<NavItemViewModel> NavItems { get; }

    public string SessionLabel => _services.App.SessionLabel;
    public string Version => _services.App.Version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCollapsed), nameof(ShowChatBadge))]
    private bool _isExpanded;

    public bool IsCollapsed => !IsExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentTab))]
    private TabId _selectedTab = TabId.Home;

    /// <summary>The view model of the active tab. The view picks its screen from the type.</summary>
    public object CurrentTab => SelectedTab switch
    {
        TabId.Home => Home,
        TabId.Network => Hubs,
        TabId.Session => Session,
        TabId.Aircraft => Aircraft,
        TabId.Objects => Objects,
        TabId.Models => ModelMatching,
        TabId.FlightPlan => FlightPlan,
        TabId.Recorder => Recorder,
        TabId.Chat => Chat,
        TabId.Monitor => Monitor,
        _ => Settings,
    };

    /// <summary>Unread chat. Shows the dot in the sidebar, and in the title bar while the window is collapsed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowChatBadge))]
    private bool _hasNewChat;

    public bool ShowChatBadge => IsCollapsed && HasNewChat;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOverlayOpen))]
    private OverlayViewModel? _overlay;

    public bool IsOverlayOpen => Overlay is not null;

    private PasswordPromptViewModel? _passwordPrompt;

    // How long the label of a joined hub may outlive a network that never started, in polls (about three seconds).
    private const int LabelGracePolls = 12;
    private bool _labelSawNetwork;
    private int _labelIdlePolls;

    private void ShowJoinedHubLabel(string name)
    {
        _labelSawNetwork = false;
        _labelIdlePolls = 0;
        AddressBook.ShowTransient(name);
    }

    private void ClearJoinedHubLabel() => AddressBook.ClearTransient();

    /// <summary>
    /// The label belongs to the hub that was joined, so it goes when the network has left it. A join takes a moment to show
    /// as connecting, so the network must have been seen on its way, or the grace has run out, before "disconnected" counts.
    /// </summary>
    private void FollowJoinedHubLabel()
    {
        if (AddressBook.TransientLabel is null)
            return;

        if (!Network.IsDisconnected)
        {
            _labelSawNetwork = true;
            _labelIdlePolls = 0;
        }
        else if (_labelSawNetwork || ++_labelIdlePolls > LabelGracePolls)
        {
            ClearJoinedHubLabel();
        }
    }
    private int _polls;

    // The live lists are re-read once a second (every fourth poll) and only while they are on screen.
    private const int LiveListEvery = 4;

    /// <summary>
    /// Brings what the live app does by itself onto the screen: the state of the Simulator and Network buttons, and a hub asking for its
    /// password. Call it regularly from the UI thread (every quarter of a second is plenty). Does nothing new on the fakes.
    /// </summary>
    public void Poll()
    {
        _services.Simulator.Poll();
        _services.Network.Poll();
        _services.SimBrief.Poll();
        Simulator.Sync(_services.Simulator.State);
        Network.Sync(_services.Network.State);
        FlightPlanLoad.Sync(_services.SimBrief.State);
        ShowPasswordRequest();
        FollowJoinedHubLabel();
        Hubs.SyncJoined();
        FollowChat();

        // the playhead moves as the take plays, so the transport is read every time while it is shown
        if (IsExpanded && SelectedTab == TabId.Recorder)
            Recorder.Refresh();

        if (++_polls % LiveListEvery == 0)
            RefreshVisibleTab();
    }

    /// <summary>
    /// The chat is read often while it is on screen (what is said should appear as it is said), and it is all read there. Elsewhere only
    /// whether something came that has not been seen is asked, for the dot.
    /// </summary>
    private void FollowChat()
    {
        if (IsExpanded && SelectedTab == TabId.Chat)
        {
            Chat.Refresh();
            _services.Chat.MarkRead();
            HasNewChat = false;
        }
        else
        {
            HasNewChat = _services.Chat.HasUnread;
        }
    }

    /// <summary>Reads the live data of the tab that is on screen. Nothing is read for a tab nobody is looking at.</summary>
    private void RefreshVisibleTab()
    {
        if (!IsExpanded)
            return;

        switch (SelectedTab)
        {
            case TabId.Home:
                Home.Refresh();
                break;
            case TabId.Network:
                Hubs.Refresh();
                break;
            case TabId.Session:
                Session.Refresh();
                break;
            case TabId.Aircraft:
                Aircraft.Refresh();
                break;
            case TabId.Models:
                ModelMatching.Refresh();
                break;
            case TabId.Monitor:
                Monitor.Refresh();
                break;
            case TabId.Recorder:
                Recorder.Refresh();
                break;
            case TabId.FlightPlan:
                FlightPlan.Refresh();
                break;
            case TabId.Settings:
                // The files are asked of the simulator, so only while the card that lists them is open.
                if (Settings.Variables.IsOpen)
                    Settings.Variables.Refresh();
                break;
            case TabId.Objects:
                Objects.Refresh();
                break;
        }
    }

    private void ShowPasswordRequest()
    {
        string? hub = _services.Network.PasswordRequestedBy;
        if (hub is null)
            return;

        // Asked already and still showing; or the user is in the middle of something else, so ask again at the next poll.
        if (_passwordPrompt is not null && ReferenceEquals(Overlay, _passwordPrompt))
            return;
        if (Overlay is not null)
            return;

        _passwordPrompt = new PasswordPromptViewModel(
            hub,
            password =>
            {
                _services.Network.SubmitPassword(password);
                return Task.CompletedTask;
            },
            onCancel: _services.Network.CancelPasswordRequest);
        ShowOverlay(_passwordPrompt);
    }

    partial void OnIsExpandedChanged(bool value) => RefreshVisibleTab();

    partial void OnSelectedTabChanged(TabId value)
    {
        RefreshNav();
        RefreshVisibleTab(); // a tab opens on what is true now, not on what it saw last
    }
    partial void OnHasNewChatChanged(bool value) => RefreshNav();

    [RelayCommand]
    private void ToggleExpand() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void OpenChat() => GoTo(TabId.Chat);

    [RelayCommand]
    private void OpenAbout() =>
        ShowOverlay(new AboutViewModel(_services.App, _services.Updates.CheckForUpdate(), _services.Platform));

    /// <summary>What the strip's Network button does while disconnected: join the hub picked in the strip.</summary>
    private Task JoinSelectedAsync() =>
        AddressBook.EffectiveSelection is { } selected ? JoinAsync(selected.Entry) : Task.CompletedTask;

    // --- IShell ---

    public void GoTo(TabId tab)
    {
        IsExpanded = true; // first, so the tab that opens sees a window that is showing it
        SelectedTab = tab;
        if (tab == TabId.Chat)
        {
            _services.Chat.MarkRead();
            HasNewChat = false;
        }
    }

    public void ShowOverlay(OverlayViewModel overlay)
    {
        overlay.CloseRequested += OnOverlayCloseRequested;
        Overlay = overlay;
    }

    public async Task JoinAsync(AddressBookEntry hub)
    {
        if (Network.IsConnecting)
            return;

        if (hub.RequiresPassword)
        {
            ShowOverlay(new PasswordPromptViewModel(hub.Name, password => ConnectToAsync(hub, password)));
            return;
        }
        await ConnectToAsync(hub, password: null);
    }

    // True from the moment a mesh of our own was asked for until a hub is joined again: the picker's hub is not what is connected then.
    private bool _onOwnMesh;

    public string? JoinedHubName =>
        Network.IsConnected && !_onOwnMesh ? AddressBook.TransientLabel ?? AddressBook.Selected?.Name : null;

    public async Task LeaveAsync()
    {
        if (Network.IsConnected)
            await Network.DisconnectAsync();
        Hubs.SyncJoined();
    }

    public async Task<string?> CreateMeshAsync()
    {
        if (Network.IsConnecting)
            return null;

        _onOwnMesh = true;
        // A mesh of your own is not a hub of the directory: the picker goes back to its pick.
        ClearJoinedHubLabel();

        string? code = null;
        _networkAction = async ct => code = await _services.Network.CreateMeshAsync(ct);
        if (Network.IsConnected)
            await Network.DisconnectAsync();
        await Network.ConnectAsync();
        return code;
    }

    private async Task ConnectToAsync(AddressBookEntry hub, string? password)
    {
        _onOwnMesh = false;

        // The picker shows the hub that was joined: its entry when the address book has it, otherwise just its name as the current text.
        if (AddressBook.Select(hub.Name))
            ClearJoinedHubLabel();
        else
            ShowJoinedHubLabel(hub.Name);
        _networkAction = ct => _services.Network.JoinAsync(hub, password, ct);

        if (Network.IsConnected)
            await Network.DisconnectAsync();
        await Network.ConnectAsync();
        Hubs.SyncJoined();
    }

    private void OnOverlayCloseRequested(object? sender, EventArgs e)
    {
        if (sender is not OverlayViewModel overlay)
            return;
        overlay.CloseRequested -= OnOverlayCloseRequested;
        // Another overlay may already have replaced this one (a prompt that opens the next step).
        if (ReferenceEquals(Overlay, overlay))
            Overlay = null;
    }

    private void RefreshNav()
    {
        foreach (NavItemViewModel item in NavItems)
        {
            item.IsActive = item.Tab == SelectedTab;
            item.ShowBadge = item.Tab == TabId.Chat && HasNewChat;
        }
    }
}
