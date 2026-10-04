using JoinFS.UI.Models;

namespace JoinFS.UI.Services;

// One small interface per concern, so the real JoinFS adapters can replace the fakes one at a time
// (Sim goes through ISimSink/ISimView, the network through Network.Snapshot and its mailbox; see CLAUDE.md "Threads").
// Nothing here may touch Main, a form or Settings directly.

/// <summary>
/// What the Simulator and Network buttons need in common. A link that <see cref="ReportsState"/> is the live app: its
/// <see cref="State"/> is the truth and changes by itself (the sim drops, the session is lost), so the UI only asks for a change and
/// then shows what <see cref="State"/> says. A link that does not is a fake that does exactly what it is asked.
/// </summary>
public interface IConnectionLink
{
    bool ReportsState { get; }

    ConnectionState State { get; }

    /// <summary>Housekeeping the live app needs, called from the UI thread before <see cref="State"/> is read (every poll).</summary>
    void Poll();
}

public interface ISimulatorLink : IConnectionLink
{
    Task ConnectAsync(CancellationToken cancellationToken);
    Task DisconnectAsync();
}

public interface INetworkLink : IConnectionLink
{
    /// <summary>
    /// The hub (or session) that is waiting for a password, or null. Joining a protected session is a conversation: the
    /// join goes out, the session answers "password required", and only then is a password asked for.
    /// </summary>
    string? PasswordRequestedBy { get; }

    /// <summary>Answers <see cref="PasswordRequestedBy"/>: joins again with this password.</summary>
    void SubmitPassword(string password);

    /// <summary>Gives up on <see cref="PasswordRequestedBy"/>.</summary>
    void CancelPasswordRequest();

    /// <summary>The code others use to join this node's mesh. Shown on the Network Hubs tab.</summary>
    string MeshCode { get; }

    /// <summary>Joins <paramref name="hub"/>. The password is null unless the hub asked for one.</summary>
    Task JoinAsync(AddressBookEntry hub, string? password, CancellationToken cancellationToken);

    /// <summary>Leaves the current network and starts a new mesh. Returns the code others use to join it.</summary>
    Task<string> CreateMeshAsync(CancellationToken cancellationToken);

    Task DisconnectAsync();
}

/// <summary>The hubs in the picker, and which one is selected. Edited from Settings → Address Book.</summary>
public interface IAddressBookStore
{
    (IReadOnlyList<AddressBookEntry> Entries, string? SelectedName) Load();
    void Save(IReadOnlyList<AddressBookEntry> entries, string? selectedName);
}

public interface IHubDirectory
{
    Task<IReadOnlyList<HubInfo>> GetPublicHubsAsync(CancellationToken cancellationToken);
}

public interface ISessionSource
{
    /// <summary>This node first (when connected), then the other users.</summary>
    IReadOnlyList<PeerInfo> GetPeers();

    PeerSettings GetSettings(string peerId);

    /// <summary>Lets this user get into your cockpit.</summary>
    void SetCockpitEntry(string peerId, bool allowed);

    /// <summary>Hands your flight controls to this user.</summary>
    void SetHandOverControls(string peerId, bool handedOver);

    /// <summary>Lets this user share more than one object.</summary>
    void SetMultipleObjects(string peerId, bool allowed);

    /// <summary>Adds the user to the address book, or removes them from it.</summary>
    void SetSaved(string peerId, bool saved);

    void SetIgnored(string peerId, bool ignored);
}

public interface ITrafficSource
{
    IReadOnlyList<AircraftInfo> GetAircraft();
    IReadOnlyList<ObjectInfo> GetObjects();
}

public interface IModelCatalog
{
    /// <summary>The model-matching table. Overrides the user saved come back through <see cref="ISettingsStore"/>.</summary>
    IReadOnlyList<ModelRule> GetDefaultRules();
    IReadOnlyList<string> GetTypes();
    IReadOnlyList<string> GetVariations();
    IReadOnlyList<ExplainRow> Explain(string model);
    IReadOnlyList<string> ExplainSteps(string model);
}

public interface IVariablesCatalog
{
    IReadOnlyList<VariableAssignment> GetAssignments();
}

/// <summary>The flight plan filed for the simulator's user aircraft.</summary>
public interface IFlightPlanStore
{
    FlightPlanData Load();
    void Save(FlightPlanData plan);
}

public interface ISimBriefClient
{
    Task<FlightPlanData> FetchAsync(string username, CancellationToken cancellationToken);
}

public interface IChatSource
{
    IReadOnlyList<ChatMessage> GetMessages();
    void Send(string text);

    /// <summary>True while messages arrived that the user has not seen. Drives the unread dot.</summary>
    bool HasUnread { get; }
    void MarkRead();
}

public interface IRecorderSource
{
    /// <summary>Callsigns that are included in the recording to start with.</summary>
    IReadOnlyCollection<string> GetRecordedCallsigns();
    IReadOnlyList<RecordedAircraft> GetLoadedRecording();
    string LoadedRecordingName { get; }
    int LoadedRecordingSeconds { get; }
}

public interface IMonitorSource
{
    IReadOnlyList<string> GetLogLines();
    int FramesPerSecond { get; }
}

/// <summary>What the X-Plane "Scan For Models" dialog starts from and looks at on disk (XPLANE build only).</summary>
public interface IXPlaneScanSource
{
    string SimFolder { get; }

    /// <summary>Aircraft folders that were scanned last time.</summary>
    IReadOnlyList<string> InitialScanFolders { get; }

    /// <summary>The folders under the X-Plane folder's Aircraft folder. Empty when there is no such folder.</summary>
    IReadOnlyList<string> ListAircraftFolders(string xplaneFolder);
}

/// <summary>Installs the JoinFS plugin into an X-Plane folder (XPLANE build only).</summary>
public interface IXPlanePluginInstaller
{
    /// <summary>The X-Plane folder used last time, or empty.</summary>
    string SavedFolder { get; }
    Task InstallAsync(string folder, CancellationToken cancellationToken);
}

public interface IUpdateChecker
{
    /// <summary>Null when this build is current.</summary>
    UpdateInfo? CheckForUpdate();
}

public interface IAppInfo
{
    string Version { get; }
    /// <summary>Title-bar label, e.g. "JoinFS-FS2024".</summary>
    string SessionLabel { get; }
    string DocumentationUrl { get; }
    string DownloadUrl { get; }
    string Copyright { get; }

    /// <summary>True only in the XPLANE build. The X-Plane settings and its label options are hidden everywhere else.</summary>
    bool IsXPlaneBuild { get; }
}

/// <summary>What the user keeps between runs. Persisted once provided, never re-asked (README: onboarding, SimBrief username).</summary>
public sealed class UserSettings
{
    public bool Onboarded { get; set; }
    public string Nickname { get; set; } = "";
    public string? SimbriefUsername { get; set; }
    public Dictionary<string, string> ModelOverrides { get; set; } = new();
    public Dictionary<string, int> HeightAdjustmentsCm { get; set; } = new();

    // The broadcast options shared by Settings → Simulator and the Objects tab.
    public bool BroadcastTacpack { get; set; }
    public bool BroadcastEverything { get; set; }

    // The model-scan options. "Scan at launch" in the X-Plane dialog and "Model scan on connect" in Settings are one setting.
    public bool ModelScanOnConnect { get; set; }
    public bool GenerateCsl { get; set; }
    public bool SkipCsl { get; set; }
}

public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}

/// <summary>What only the window can do: clipboard, files, the browser.</summary>
public interface IPlatform
{
    Task CopyTextAsync(string text);
    void OpenUrl(string url);
    Task<string?> PickOpenFileAsync(string title);
    Task<string?> PickSaveFileAsync(string title, string suggestedName);
    Task<string?> PickFolderAsync(string title);
}

/// <summary>Everything the view models need from the outside. Built once at startup.</summary>
public sealed record AppServices(
    ISimulatorLink Simulator,
    INetworkLink Network,
    IAddressBookStore AddressBook,
    IHubDirectory Hubs,
    ISessionSource Session,
    ITrafficSource Traffic,
    IModelCatalog Models,
    IVariablesCatalog Variables,
    ISimBriefClient SimBrief,
    IFlightPlanStore FlightPlan,
    IChatSource Chat,
    IRecorderSource Recorder,
    IMonitorSource Monitor,
    IXPlanePluginInstaller XPlanePlugin,
    IXPlaneScanSource XPlaneScan,
    IUpdateChecker Updates,
    IAppInfo App,
    ISettingsStore Settings,
    IPlatform Platform);
