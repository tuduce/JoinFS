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
    /// <summary>The hubs the app knows of, online or not, ignored or not. The app keeps the list; this reads it.</summary>
    IReadOnlyList<HubInfo> GetHubs();

    void SetIgnored(string hubId, bool ignored);

    /// <summary>Adds the hub to the address book, or removes it from it.</summary>
    void SetSaved(string hubId, bool saved);
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

    /// <summary>Also list the aircraft of the other public hubs (not the one you are in).</summary>
    bool IncludeHubAircraft { get; set; }

    /// <summary>Also list the AI aircraft of the local simulator.</summary>
    bool IncludeSimulatorAircraft { get; set; }

    /// <summary>True while you are in another aircraft's cockpit: Enter Cockpit then means Leave.</summary>
    bool InCockpit { get; }

    /// <summary>True while an aircraft is tracked, so Stop Tracking has something to stop.</summary>
    bool IsTracking { get; }

    void SetRecording(string aircraftId, bool recording);
    void SetIgnored(string aircraftId, bool ignored);
    void Follow(string aircraftId);

    /// <summary>Enters the cockpit of the aircraft, or leaves the one you are in.</summary>
    void EnterCockpit(string aircraftId);

    void TrackHeading(string aircraftId);
    void TrackBearing(string aircraftId);
    void StopTracking();

    /// <summary>Takes the weather the aircraft reports.</summary>
    void CopyWeather(string aircraftId);

    /// <summary>Merge the objects of one owner and model into one row with a count.</summary>
    bool GroupObjects { get; set; }

    /// <summary>Broadcasts or stops broadcasting one object. Not for a group; not for objects of the network.</summary>
    void SetObjectBroadcast(string objectId, bool broadcast);

    /// <summary>Broadcasts or stops broadcasting every object of a model, and the objects of its kind that come later.</summary>
    void SetModelBroadcast(string originalModel, bool broadcast);

    void SetIgnoreOwner(string objectId, bool ignored);
    void SetIgnoreModel(string objectId, bool ignored);

    IReadOnlyList<ObjectInfo> GetObjects();

    /// <summary>The model of an aircraft as its owner has it, for Substitute. Null when the aircraft is gone or has none.</summary>
    ModelTarget? GetAircraftModel(string aircraftId);

    /// <summary>The model of an object (of a group, the model they share), for Substitute. Null when the object is gone.</summary>
    ModelTarget? GetObjectModel(string objectId);

    /// <summary>
    /// How the model of an aircraft was chosen. For your own aircraft the matching is run now, as a preview that changes nothing.
    /// Null when there is nothing to explain: the aircraft is gone, or its model was never matched.
    /// </summary>
    Task<MatchExplanation?> ExplainMatchAsync(string aircraftId);

    /// <summary>
    /// The height adjustment of the model the aircraft shows. Null when there is nothing to adjust: only a model that stands in for
    /// the owner's, on an aircraft JoinFS creates, has an adjustment.
    /// </summary>
    HeightAdjustment? GetHeightAdjustment(string aircraftId);

    /// <summary>Sets the adjustment of the model the aircraft shows, and keeps it. Zero turns it off.</summary>
    void SetHeightAdjustment(string aircraftId, int centimetres);

    /// <summary>The title of the model the aircraft shows, which its variable files are kept for. Null when the aircraft is gone.</summary>
    string? GetVariablesModel(string aircraftId);
}

/// <summary>
/// The models the simulator has and how one stands in for another: the match table of the Model Matching tab, and the picker
/// that Substitute (and Variables) choose a model with.
/// </summary>
public interface IModelCatalog
{
    /// <summary>The model-matching table: the default rules first, then the matches that were added.</summary>
    IReadOnlyList<ModelRule> GetRules();

    /// <summary>False until the simulator's models are known; there is then nothing to pick from.</summary>
    bool HasModels { get; }

    /// <summary>The types of the models that have every word of <paramref name="filter"/> in their manufacturer, type or variation.</summary>
    IReadOnlyList<string> GetTypes(string filter);

    IReadOnlyList<string> GetVariations(string type);

    /// <summary>The model of this type and variation as it is shown once chosen, or empty when there is none.</summary>
    string GetReplacement(string type, string variation);

    /// <summary>The title of the model of this type and variation, or empty when there is none.</summary>
    string GetTitle(string type, string variation);

    /// <summary>The type and variation of the model with this title, or null when the simulator has no such model.</summary>
    ModelChoice? FindChoice(string title);

    /// <summary>What stands in for the model now, to start the picker on. Null when nothing is known.</summary>
    Task<ModelChoice?> GetCurrentAsync(ModelTarget target);

    /// <summary>Makes the model of this type and variation stand in for the target.</summary>
    void SetSubstitute(ModelTarget target, string type, string variation);

    /// <summary>The model stands for itself again.</summary>
    void ClearSubstitute(ModelTarget target);

    /// <summary>How the models stand, in a line for the Model Matching tab: how many are known, or that a scan is running.</summary>
    string ScanStatus { get; }

    /// <summary>The file that lists the models the simulator has, or null when it has not been written yet.</summary>
    string? KnownModelsFile();

    /// <summary>Writes a zip with the match <paramref name="report"/> and the files it was made from, for a bug report.</summary>
    void WriteDebugBundle(string zipPath, string report);
}

/// <summary>
/// The variable files of each model: lists of files of simulator variables that the models use. A model has a list of its own,
/// or else the default of its kind. They take effect when the simulator is connected again.
/// </summary>
public interface IVariablesCatalog
{
    /// <summary>The models that have a list of their own, with it.</summary>
    IReadOnlyList<VariableAssignment> GetAssignments();

    /// <summary>False for X-Plane, where the model is the one given and cannot be picked.</summary>
    bool CanPickModel { get; }

    /// <summary>The folder the variable files are in. They are named relative to it.</summary>
    string FilesFolder { get; }

    /// <summary>The files of a model: its own list, or the default of its kind. Empty while the simulator is not connected.</summary>
    IReadOnlyList<string> GetFiles(string model);

    /// <summary>Adds files, named relative to <see cref="FilesFolder"/>, to the model's list. The model has a list of its own from then on.</summary>
    void AddFiles(string model, IReadOnlyList<string> files);

    void RemoveFile(string model, int index);

    /// <summary>One of the default files, which JoinFS provides and the user does not edit.</summary>
    bool IsBuiltIn(string file);

    /// <summary>Makes the changes take effect: the simulator is connected again.</summary>
    void Apply();
}

/// <summary>The flight plan filed for the simulator's user aircraft.</summary>
public interface IFlightPlanStore
{
    FlightPlanData Load();
    void Save(FlightPlanData plan);
}

/// <summary>
/// SimBrief, and the state of the last fetch, which the strip's Flight Plan button shows: fetching is connecting, a plan fetched is
/// connected, and nothing fetched yet, a failed fetch and a <see cref="Reset"/> are disconnected.
/// </summary>
public interface ISimBriefClient : IConnectionLink
{
    /// <summary>
    /// Fetches the user's latest plan. With <paramref name="commit"/> it is also made the user's plan and sent to the network, as the
    /// strip's button does; without, it is only returned, to be shown and committed with Save. Null when SimBrief has no plan for the user.
    /// </summary>
    Task<FlightPlanData?> FetchAsync(string username, bool commit, CancellationToken cancellationToken);

    /// <summary>Forgets the last fetch: the strip's button goes back to "not loaded". The plan itself stays.</summary>
    void Reset();
}

/// <summary>The chat of the session you are in: what the pilots say, and a line to say something.</summary>
public interface IChatSource
{
    /// <summary>The lines, oldest first: what was said in the session lately, and the answers JoinFS gave to commands.</summary>
    IReadOnlyList<ChatMessage> GetMessages();

    /// <summary>True while there is a session to talk in. Without one, the chat is empty and nothing can be said.</summary>
    bool IsConnected { get; }

    /// <summary>True when a message can be sent now: connected, and not just after another, which is how the old window kept from flooding.</summary>
    bool CanSend { get; }

    /// <summary>Says <paramref name="text"/> to the session. A line that starts with "." is a command, answered to you alone.</summary>
    void Send(string text);

    /// <summary>True while messages arrived that the user has not seen. Drives the unread dot.</summary>
    bool HasUnread { get; }
    void MarkRead();
}

/// <summary>
/// The recorder: a take of the aircraft that were ticked to record, which can be played back, laid over, trimmed, saved and loaded.
/// </summary>
public interface IRecorderSource
{
    /// <summary>What the recorder is doing now. Read often while the tab is shown.</summary>
    RecorderStatus GetStatus();

    /// <summary>The aircraft of the recording that is in the recorder, with whether each is left out of playback.</summary>
    IReadOnlyList<RecordedAircraft> GetLoadedRecording();

    /// <summary>The name of the file the recording was loaded from or last saved to. Empty for one just recorded.</summary>
    string LoadedRecordingName { get; }

    /// <summary>Play the recording again when it ends.</summary>
    bool Loop { get; set; }

    /// <summary>The folder recordings were last opened from or saved to, to start the file dialogs on.</summary>
    string RecordingFolder { get; }

    /// <summary>Starts a new recording of the aircraft ticked to record. What was in the recorder is gone.</summary>
    void Record();

    /// <summary>Starts playing the recording; while it plays, pauses it, and while it is paused, goes on.</summary>
    void TogglePlay();

    /// <summary>Plays the recording and records on top of it.</summary>
    void Overdub();

    void Stop();

    /// <summary>
    /// Writes the recording that is in the recorder to the documents folder under a name made of the date, the time and the first
    /// callsign, never over a file. Returns the file's name. Throws with what to tell the user when it cannot be written, and then the
    /// recording is still in the recorder. For when a dialog is not wanted, such as a shortcut pressed in VR.
    /// </summary>
    string AutoSave();

    /// <summary>Moves playback to this many seconds from the start.</summary>
    void Seek(double seconds);

    /// <summary>Cuts what is before the playhead.</summary>
    void TrimStart();

    /// <summary>Cuts what is after the playhead.</summary>
    void TrimEnd();

    /// <summary>Leaves an aircraft out of playback until the recording is loaded again. It is still saved with the recording.</summary>
    void SkipAircraft(string aircraftId);

    /// <summary>
    /// Loads a recording file, or with <paramref name="append"/> adds it after the end of the one in the recorder, and plays it.
    /// Throws with what to tell the user when the file cannot be used.
    /// </summary>
    Task OpenAsync(string path, bool append);

    Task SaveAsync(string path);
}

/// <summary>The log of what JoinFS is doing, and what is added to it.</summary>
public interface IMonitorSource
{
    /// <summary>The last lines of the log. When there are more than that, the first lines say so.</summary>
    IReadOnlyList<string> GetLogLines();

    /// <summary>How many frames the simulator draws in a second, measured since this was last read. Null when it cannot be told.</summary>
    int? FramesPerSecond { get; }

    /// <summary>Log what the network does with each object, as it happens.</summary>
    bool ShowNetwork { get; set; }

    /// <summary>Log the variables of each object, as they are recorded.</summary>
    bool ShowVariables { get; set; }

    /// <summary>Writes what the network knows of its nodes to the log, once.</summary>
    void WriteNodeStatistics();

    /// <summary>Writes how many of each kind of packet were received to the log, once.</summary>
    void WritePacketStatistics();

    /// <summary>The log files that exist: this run's and the last run's.</summary>
    IReadOnlyList<string> LogFiles { get; }
}

/// <summary>What the X-Plane "Scan For Models" dialog starts from and looks at on disk (XPLANE build only).</summary>
public interface IXPlaneScanSource
{
    string SimFolder { get; }

    /// <summary>Aircraft folders that were scanned last time.</summary>
    IReadOnlyList<string> InitialScanFolders { get; }

    /// <summary>The folders under the X-Plane folder's Aircraft folder. Empty when there is no such folder.</summary>
    IReadOnlyList<string> ListAircraftFolders(string xplaneFolder);

    /// <summary>Remembers the folders and scans them in the background. False when a scan is already running.</summary>
    bool Scan(string xplaneFolder, IReadOnlyList<string> aircraftFolders);
}

/// <summary>An add-on that Scan For Models can include.</summary>
/// <param name="Key">What the scan knows it by. It is kept in the saved choice, so it does not change.</param>
/// <param name="Name">What the user is shown.</param>
public sealed record ScanAddOn(string Key, string Name, bool Selected);

/// <summary>Where Scan For Models looks, for every simulator but X-Plane (which has <see cref="IXPlaneScanSource"/>), and what starts it.</summary>
public interface IModelScanSource
{
    string SimulatorName { get; }

    /// <summary>The simulator's folder, as chosen last time.</summary>
    string SimFolder { get; }

    /// <summary>What the folder is asked for: the Packages folder of Microsoft Flight Simulator, the root folder of the others.</summary>
    string FolderPrompt { get; }

    /// <summary>Only simulators with a single SimObjects folder have subfolders to choose from; Microsoft Flight Simulator nests them per package.</summary>
    bool ListsSubfolders { get; }

    /// <summary>The subfolders that were scanned last time.</summary>
    IReadOnlyList<string> InitialSubfolders { get; }

    /// <summary>The folders under the simulator folder's SimObjects folder. Empty when there is no such folder.</summary>
    IReadOnlyList<string> ListSubfolders(string simFolder);

    IReadOnlyList<ScanAddOn> AddOns { get; }

    /// <summary>The other folders that were scanned last time.</summary>
    IReadOnlyList<string> AdditionalFolders { get; }

    /// <summary>Remembers the choice and scans in the background. False when a scan is already running.</summary>
    bool Scan(string simFolder, IReadOnlyList<string> subfolders, IReadOnlyList<string> addOns, IReadOnlyList<string> additionalFolders);
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

/// <summary>
/// What the Settings tab edits besides the profile (nickname, SimBrief name, broadcast and scan options, which <see cref="UserSettings"/> keeps).
/// The defaults are those of a fresh install.
/// </summary>
public sealed class Preferences
{
    // User Interface
    public bool AlwaysOnTop { get; set; }
    public bool AutoRefresh { get; set; } = true;
    public bool ToolTips { get; set; } = true;
    public MapStyle MapStyle { get; set; } = MapStyle.Standard;

    // Simulator
    public bool ConnectOnLaunch { get; set; } = true;
    public bool ElevationCorrection { get; set; }
    public int CircleOfActivityNm { get; set; } = 5;
    public int FollowDistanceM { get; set; } = 80;
    public bool AutoImportSimbrief { get; set; }

    // The floating label above other aircraft. X-Plane only.
    public bool ShowNickname { get; set; } = true;
    public bool ShowCallsign { get; set; } = true;
    public bool ShowDistance { get; set; }
    public bool ShowAltitude { get; set; }
    public bool ShowSpeed { get; set; }

    /// <summary>"#RRGGBB".</summary>
    public string LabelColor { get; set; } = "#F2C400";

    // Network
    public bool ChooseOwnPort { get; set; }
    public int LocalPort { get; set; } = 40383;
    public bool JoinGlobalAtLaunch { get; set; } = true;
    public bool LowBandwidth { get; set; }
    public bool GenerateWhazzup { get; set; } = true;
    public bool WhazzupIncludeGlobalUsers { get; set; } = true;
    public bool WhazzupIncludeAi { get; set; }
    public string Password { get; set; } = "";

    // Hub Mode (Public)
    public bool HubMode { get; set; }
    public string HubDomain { get; set; } = "";
    public string HubName { get; set; } = "";
    public string HubAbout { get; set; } = "";
    public string HubVoice { get; set; } = "";
    public string HubEvent { get; set; } = "";

    // X-Plane
    public string XPlaneAddress { get; set; } = "";
    public bool Tcas { get; set; }

    public Preferences Clone() => (Preferences)MemberwiseClone();
}

/// <summary>
/// Reads and applies the <see cref="Preferences"/>. Saving applies them to the running app: a new port is opened,
/// hub mode starts or stops the hub, and so on. It is called on every change, so it must do nothing for what did not change.
/// </summary>
public interface IPreferencesStore
{
    Preferences Load();
    void Save(Preferences preferences);
}

/// <summary>What only the window can do: clipboard, files, the browser.</summary>
public interface IPlatform
{
    Task CopyTextAsync(string text);
    void OpenUrl(string url);

    /// <summary>Opens a file of the user's in the program that goes with it. Unlike <see cref="OpenUrl"/>, which is for web links only.</summary>
    Task OpenFileAsync(string path);

    /// <param name="extension">Only files of this kind are offered, without the dot ("jfs").</param>
    Task<string?> PickOpenFileAsync(string title, string? startFolder = null, string? extension = null);

    Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null, string? extension = null);
    Task<string?> PickFolderAsync(string title);
}

/// <summary>
/// The pictures the Home map is made of: slippy-map tiles (256 x 256 px, <c>z/x/y</c>, x and y counted from the top left of the
/// Web Mercator world at zoom <c>z</c>). Which map it is, and who has to be credited for it, belongs to the source.
/// </summary>
public interface IMapTileSource
{
    /// <summary>What has to be shown on the map for the data's owners, e.g. "(c) OpenStreetMap contributors".</summary>
    string Attribution { get; }

    /// <summary>Where the attribution leads, or empty.</summary>
    string AttributionUrl { get; }

    /// <summary>The highest zoom the source has tiles for.</summary>
    int MaxZoom { get; }

    /// <summary>The encoded image (PNG or JPEG) of one tile, or null when there is none and none is coming (offline, not found).</summary>
    Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken);
}

/// <summary>Which pictures the Home map is made of.</summary>
public enum MapStyle
{
    /// <summary>The usual street map of OpenStreetMap.</summary>
    Standard,

    /// <summary>Stamen's watercolor map, as the Smithsonian keeps it online.</summary>
    Watercolor,
}

/// <summary>The tile source of each <see cref="MapStyle"/>. A source is made when its style is first asked for, and kept.</summary>
public interface IMapTileProvider
{
    IMapTileSource Get(MapStyle style);
}

/// <param name="create">Makes the source of a style.</param>
public sealed class MapTileProvider(Func<MapStyle, IMapTileSource> create) : IMapTileProvider, IDisposable
{
    private readonly Dictionary<MapStyle, IMapTileSource> _sources = [];

    public IMapTileSource Get(MapStyle style)
    {
        lock (_sources)
        {
            if (!_sources.TryGetValue(style, out IMapTileSource? source))
                _sources[style] = source = create(style);
            return source;
        }
    }

    public void Dispose()
    {
        lock (_sources)
        {
            foreach (IDisposable source in _sources.Values.OfType<IDisposable>())
                source.Dispose();
            _sources.Clear();
        }
    }
}

/// <summary>
/// The global keyboard shortcuts. They work while another program, such as the simulator, has the keys, so they are looked for by asking
/// the system which keys are down, not by listening to the window.
/// </summary>
public interface IShortcutSource
{
    /// <summary>All the shortcuts, in the order of <see cref="ShortcutAction"/>.</summary>
    IReadOnlyList<ShortcutBinding> Load();

    /// <summary>Keeps the shortcut. It takes effect at once.</summary>
    void Save(ShortcutBinding binding);

    /// <summary>
    /// The enabled shortcuts whose keys went down since this was last called. Each is reported once per press. Call it often, about ten
    /// times a second, from the UI thread: a key tapped between two calls is missed.
    /// </summary>
    IReadOnlyList<ShortcutAction> TakePressed();
}

/// <summary>Messages the app wants the user to read: the ones the old forms showed in a message box.</summary>
public interface IMessageSource
{
    /// <summary>The message waiting to be shown, or null. Asking takes it: it is returned once.</summary>
    string? TakeMessage();
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
    IPlatform Platform,
    IPreferencesStore Preferences,
    IModelScanSource ModelScan,
    IMapTileProvider MapTiles,
    IMessageSource Messages,
    IShortcutSource Shortcuts);
