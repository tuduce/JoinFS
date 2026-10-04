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

        Home = new HomeViewModel(this, services.Session, services.Traffic, services.App, services.Platform);
        Hubs = new HubsViewModel(services.Hubs, services.Network, AddressBook, this, ignoredHubs: ["NoiseAbatement Hub"]);
        Session = new SessionViewModel(services.Session);
        RecordSelection = new RecordSelection(services.Recorder.GetRecordedCallsigns());
        Aircraft = new AircraftViewModel(services.Traffic, services.Models, services.Platform, Profile, RecordSelection, this);
        Objects = new ObjectsViewModel(services.Traffic, services.Models, Profile, this);
        ModelMatching = new ModelMatchingViewModel(services.Models, Profile, this);
        FlightPlan = new FlightPlanViewModel(services.FlightPlan, services.SimBrief, Profile, this);
        Recorder = new RecorderViewModel(services.Recorder, services.Traffic, RecordSelection, services.Platform);
        Chat = new ChatViewModel(services.Chat);
        Monitor = new MonitorViewModel(services.Monitor);
        Settings = new SettingsViewModel(Profile, AddressBook, services.Variables, services.XPlanePlugin, services.XPlaneScan, this, services.Platform, () => Simulator.IsConnected, services.App.IsXPlaneBuild);

        // The strip's flight-plan button fetches from SimBrief. If a username is still needed the prompt comes first and
        // the import finishes after it, so this attempt ends "not loaded" and the import itself reports back through Imported.
        // That also covers an import started from the Flight Plan tab.
        FlightPlanLoad = new ConnectionViewModel(ConnectionLabels.FlightPlan, async _ =>
        {
            if (!await FlightPlan.TryImportFromSimbriefAsync())
                throw new OperationCanceledException();
        });
        FlightPlan.Imported += (_, _) => FlightPlanLoad.SetState(ConnectionState.Connected);

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

    /// <summary>
    /// Brings what the live app does by itself onto the screen: the state of the Simulator and Network buttons, and a hub asking for its
    /// password. Call it regularly from the UI thread (every quarter of a second is plenty). Does nothing new on the fakes.
    /// </summary>
    public void Poll()
    {
        _services.Simulator.Poll();
        _services.Network.Poll();
        Simulator.Sync(_services.Simulator.State);
        Network.Sync(_services.Network.State);
        ShowPasswordRequest();
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

    partial void OnSelectedTabChanged(TabId value) => RefreshNav();
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
        AddressBook.Selected is { } selected ? JoinAsync(selected.Entry) : Task.CompletedTask;

    // --- IShell ---

    public void GoTo(TabId tab)
    {
        SelectedTab = tab;
        IsExpanded = true;
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

    public async Task<string?> CreateMeshAsync()
    {
        if (Network.IsConnecting)
            return null;

        string? code = null;
        _networkAction = async ct => code = await _services.Network.CreateMeshAsync(ct);
        if (Network.IsConnected)
            await Network.DisconnectAsync();
        await Network.ConnectAsync();
        return code;
    }

    private async Task ConnectToAsync(AddressBookEntry hub, string? password)
    {
        // The picker shows the hub that was joined, when the address book has it.
        AddressBook.Select(hub.Name);
        _networkAction = ct => _services.Network.JoinAsync(hub, password, ct);

        if (Network.IsConnected)
            await Network.DisconnectAsync();
        await Network.ConnectAsync();
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
