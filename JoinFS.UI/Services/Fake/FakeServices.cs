using JoinFS.UI.Models;

namespace JoinFS.UI.Services.Fake;

/// <summary>In-memory stand-ins for every service, driven by <see cref="SampleData"/>. Used until the real JoinFS adapters exist.</summary>
public static class FakeServices
{
    /// <param name="latency">How long a connect or fetch takes. The prototype uses 900 ms; tests pass zero.</param>
    /// <param name="xplaneBuild">Pretend to be the XPLANE build, which shows the X-Plane settings.</param>
    public static AppServices Create(TimeSpan latency, UserSettings? settings = null, IPlatform? platform = null, bool xplaneBuild = false)
    {
        InMemoryAddressBookStore addressBook = new();
        return Build(latency, settings, platform, xplaneBuild, addressBook);
    }

    private static AppServices Build(TimeSpan latency, UserSettings? settings, IPlatform? platform, bool xplaneBuild, InMemoryAddressBookStore addressBook) => new(
        Simulator: new FakeSimulatorLink(latency),
        Network: new FakeNetworkLink(latency),
        AddressBook: addressBook,
        Hubs: new FakeHubDirectory(addressBook),
        Session: new FakeSessionSource(addressBook),
        Traffic: new FakeTrafficSource(),
        Models: new FakeModelCatalog(),
        Variables: new FakeVariablesCatalog(),
        SimBrief: new FakeSimBriefClient(latency),
        FlightPlan: new InMemoryFlightPlanStore(),
        Chat: new FakeChatSource(),
        Recorder: new FakeRecorderSource(),
        Monitor: new FakeMonitorSource(),
        XPlanePlugin: new FakeXPlanePluginInstaller(),
        XPlaneScan: new FakeXPlaneScanSource(),
        Updates: new FakeUpdateChecker(),
        App: new FakeAppInfo(xplaneBuild),
        Settings: new InMemorySettingsStore(settings),
        Platform: platform ?? new NullPlatform(),
        Preferences: new InMemoryPreferencesStore(),
        ModelScan: new FakeModelScanSource(),
        MapTiles: new NoMapTiles(),
        Messages: new NoMessages(),
        Shortcuts: new FakeShortcutSource());
}

public sealed class FakeSimulatorLink(TimeSpan latency) : ISimulatorLink
{
    public bool ReportsState => false;
    public ConnectionState State => ConnectionState.Disconnected;
    public void Poll() { }

    public Task ConnectAsync(CancellationToken cancellationToken) => Task.Delay(latency, cancellationToken);
    public Task DisconnectAsync() => Task.CompletedTask;
}

public sealed class FakeNetworkLink(TimeSpan latency) : INetworkLink
{
    public bool ReportsState => false;
    public ConnectionState State => ConnectionState.Disconnected;
    public void Poll() { }

    public string? PasswordRequestedBy => null;
    public void SubmitPassword(string password) { }
    public void CancelPasswordRequest() { }

    public string MeshCode { get; private set; } = "40383 51901";

    public Task JoinAsync(AddressBookEntry hub, string? password, CancellationToken cancellationToken) => Task.Delay(latency, cancellationToken);

    public async Task<string> CreateMeshAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken);
        MeshCode = $"{Random.Shared.Next(10000, 99999)} {Random.Shared.Next(10000, 99999)}";
        return MeshCode;
    }

    public Task DisconnectAsync() => Task.CompletedTask;
}

public sealed class InMemoryAddressBookStore : IAddressBookStore
{
    private IReadOnlyList<AddressBookEntry> _entries = SampleData.AddressBook;
    private string? _selectedName = "Planet FsHub";

    public (IReadOnlyList<AddressBookEntry> Entries, string? SelectedName) Load() => (_entries, _selectedName);

    public void Save(IReadOnlyList<AddressBookEntry> entries, string? selectedName)
    {
        _entries = entries;
        _selectedName = selectedName;
    }
}

public sealed class FakeHubDirectory(IAddressBookStore addressBook) : IHubDirectory
{
    private readonly Dictionary<string, bool> _ignored = [];

    public IReadOnlyList<HubInfo> GetHubs()
    {
        HashSet<string> saved = [.. addressBook.Load().Entries.Select(e => e.Name)];
        return [.. SampleData.Hubs.Select(h => h with { Ignored = _ignored.GetValueOrDefault(h.Id, h.Ignored), Saved = saved.Contains(h.Name) })];
    }

    public void SetIgnored(string hubId, bool ignored) => _ignored[hubId] = ignored;

    public void SetSaved(string hubId, bool saved)
    {
        HubInfo? hub = SampleData.Hubs.FirstOrDefault(h => h.Id == hubId);
        if (hub is null)
            return;

        (IReadOnlyList<AddressBookEntry> entries, string? selected) = addressBook.Load();
        List<AddressBookEntry> next = [.. entries.Where(e => e.Name != hub.Name)];
        if (saved)
            next.Add(new AddressBookEntry(hub.Name, hub.Address, RequiresPassword: hub.Status == HubStatus.Password));
        addressBook.Save(next, selected);
    }
}

public sealed class FakeSessionSource(IAddressBookStore addressBook) : ISessionSource
{
    private readonly Dictionary<string, PeerSettings> _settings = [];

    public IReadOnlyList<PeerInfo> GetPeers() => SampleData.Peers;

    public PeerSettings GetSettings(string peerId) =>
        (_settings.GetValueOrDefault(peerId) ?? new PeerSettings(false, false, false, false, false)) with { IsSaved = IsSaved(peerId) };

    public void SetCockpitEntry(string peerId, bool allowed) => Update(peerId, s => s with { CockpitEntry = allowed });
    public void SetHandOverControls(string peerId, bool handedOver) => Update(peerId, s => s with { HandOverControls = handedOver });
    public void SetMultipleObjects(string peerId, bool allowed) => Update(peerId, s => s with { MultipleObjects = allowed });
    public void SetIgnored(string peerId, bool ignored) => Update(peerId, s => s with { IsIgnored = ignored });

    public void SetSaved(string peerId, bool saved)
    {
        (IReadOnlyList<AddressBookEntry> entries, string? selected) = addressBook.Load();
        List<AddressBookEntry> next = [.. entries.Where(e => e.Name != peerId)];
        if (saved)
            next.Add(new AddressBookEntry(peerId, peerId));
        addressBook.Save(next, selected);
    }

    private bool IsSaved(string peerId) => addressBook.Load().Entries.Any(e => e.Name == peerId);

    private void Update(string peerId, Func<PeerSettings, PeerSettings> change) =>
        _settings[peerId] = change(GetSettings(peerId));
}

public sealed class FakeTrafficSource : ITrafficSource
{
    private readonly Dictionary<string, bool> _recording = [];
    private readonly Dictionary<string, bool> _ignored = [];
    private string? _tracked;

    public bool IncludeHubAircraft { get; set; }
    public bool IncludeSimulatorAircraft { get; set; }
    public bool InCockpit { get; private set; }
    public bool IsTracking => _tracked is not null;

    public IReadOnlyList<AircraftInfo> GetAircraft()
    {
        IEnumerable<AircraftInfo> all = SampleData.Aircraft;
        if (IncludeHubAircraft)
            all = all.Concat(SampleData.HubAircraft);
        if (IncludeSimulatorAircraft)
            all = all.Concat(SampleData.SimulatorAircraft);

        return [.. all.Select(a => a with
        {
            Recording = _recording.GetValueOrDefault(a.Id, a.Recording),
            Ignored = _ignored.GetValueOrDefault(a.Id, a.Ignored),
            Tracked = a.Id == _tracked,
        })];
    }

    public void SetRecording(string aircraftId, bool recording) => _recording[aircraftId] = recording;
    public void SetIgnored(string aircraftId, bool ignored) => _ignored[aircraftId] = ignored;
    public void Follow(string aircraftId) { }
    public void EnterCockpit(string aircraftId) => InCockpit = !InCockpit;
    public void TrackHeading(string aircraftId) => _tracked = aircraftId;
    public void TrackBearing(string aircraftId) => _tracked = aircraftId;
    public void StopTracking() => _tracked = null;
    public void CopyWeather(string aircraftId) { }
    private readonly Dictionary<string, (bool? Broadcast, bool? IgnoreOwner, bool? IgnoreModel)> _object = [];
    private readonly HashSet<string> _modelBroadcast = [];

    public bool GroupObjects { get; set; }

    public IReadOnlyList<ObjectInfo> GetObjects()
    {
        IEnumerable<ObjectInfo> rows = SampleData.Objects.Select(o =>
        {
            var state = _object.GetValueOrDefault(o.Id);
            return o with
            {
                Broadcast = state.Broadcast ?? o.Broadcast,
                IgnoreOwner = state.IgnoreOwner ?? o.IgnoreOwner,
                IgnoreModel = state.IgnoreModel ?? o.IgnoreModel,
                ModelBroadcast = _modelBroadcast.Contains(o.OriginalModel),
            };
        });

        if (!GroupObjects)
            return [.. rows];

        // One row per owner and model, with the count, like the old window's grouping.
        return [.. rows.GroupBy(o => (o.Owner, o.Model)).Select(g => g.First() with
        {
            Id = "group:" + g.Key.Owner + "/" + g.Key.Model,
            Count = g.Sum(o => o.Count),
            Bearing = null,
            DistanceNm = null,
            Broadcast = g.First().ModelBroadcast,
        })];
    }

    public ModelTarget? GetAircraftModel(string aircraftId)
    {
        AircraftInfo? aircraft = SampleData.Aircraft.Concat(SampleData.HubAircraft).Concat(SampleData.SimulatorAircraft).FirstOrDefault(a => a.Id == aircraftId);
        return aircraft is null ? null : new ModelTarget(aircraft.OriginalModel);
    }

    public string? GetVariablesModel(string aircraftId) =>
        SampleData.Aircraft.Concat(SampleData.HubAircraft).Concat(SampleData.SimulatorAircraft).FirstOrDefault(a => a.Id == aircraftId)?.Model;

    public Task<MatchExplanation?> ExplainMatchAsync(string aircraftId)
    {
        AircraftInfo? aircraft = SampleData.Aircraft.Concat(SampleData.HubAircraft).Concat(SampleData.SimulatorAircraft).FirstOrDefault(a => a.Id == aircraftId);
        if (aircraft is null)
            return Task.FromResult<MatchExplanation?>(null);

        ExplainRow[] rows =
        [
            new("Manufacturer", "Generic", aircraft.Model.Split(' ')[0]),
            new("Category", "SingleProp", "SingleProp", Decisive: true),
            new("Livery", "Default", "Closest available"),
            new("ICAO Type", "Unknown", "Fallback"),
        ];
        string[] steps =
        [
            "1. Exact title match - not found.",
            "2. ICAO + livery match - not found.",
            "3. ICAO match, any livery - not found.",
            $"4. Category fallback match - matched \"{aircraft.Model}\".",
        ];
        return Task.FromResult<MatchExplanation?>(new MatchExplanation(
            aircraft.Callsign, $"Result: Default - matched '{aircraft.Model}'", null, rows, steps, "Models come from the fake catalog.",
            $"# Match Report - {aircraft.Callsign}"));
    }

    private readonly Dictionary<string, int> _heights = [];

    public HeightAdjustment? GetHeightAdjustment(string aircraftId)
    {
        AircraftInfo? aircraft = SampleData.Aircraft.Concat(SampleData.HubAircraft).Concat(SampleData.SimulatorAircraft).FirstOrDefault(a => a.Id == aircraftId);
        return aircraft is null ? null : new HeightAdjustment(aircraft.Model, _heights.GetValueOrDefault(aircraft.Model));
    }

    public void SetHeightAdjustment(string aircraftId, int centimetres)
    {
        if (GetHeightAdjustment(aircraftId) is { } current)
            _heights[current.Model] = centimetres;
    }

    public ModelTarget? GetObjectModel(string objectId)
    {
        ObjectInfo? obj = SampleData.Objects.FirstOrDefault(o => o.Id == objectId || "group:" + o.Owner + "/" + o.Model == objectId);
        return obj is null ? null : new ModelTarget(obj.OriginalModel);
    }

    public void SetObjectBroadcast(string objectId, bool broadcast) => Set(objectId, s => (broadcast, s.IgnoreOwner, s.IgnoreModel));
    public void SetIgnoreOwner(string objectId, bool ignored) => Set(objectId, s => (s.Broadcast, ignored, s.IgnoreModel));
    public void SetIgnoreModel(string objectId, bool ignored) => Set(objectId, s => (s.Broadcast, s.IgnoreOwner, ignored));

    public void SetModelBroadcast(string originalModel, bool broadcast)
    {
        if (broadcast)
            _modelBroadcast.Add(originalModel);
        else
            _modelBroadcast.Remove(originalModel);
    }

    private void Set(string objectId, Func<(bool? Broadcast, bool? IgnoreOwner, bool? IgnoreModel), (bool? Broadcast, bool? IgnoreOwner, bool? IgnoreModel)> change) =>
        _object[objectId] = change(_object.GetValueOrDefault(objectId));
}

public sealed class FakeModelCatalog : IModelCatalog
{
    private const string Separator = " [+] ";
    private readonly List<ModelRule> _rules = [.. SampleData.DefaultRules];

    public IReadOnlyList<ModelRule> GetRules() => [.. _rules];

    public bool HasModels => true;

    public IReadOnlyList<string> GetTypes(string filter)
    {
        string[] words = filter.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return [.. SampleData.ModelTypes.Where(t => words.All(w => t.Contains(w, StringComparison.OrdinalIgnoreCase)))];
    }

    public IReadOnlyList<string> GetVariations(string type) => SampleData.ModelVariations;

    public string GetReplacement(string type, string variation) => $"{type}{Separator}{variation}";

    public Task<ModelChoice?> GetCurrentAsync(ModelTarget target)
    {
        ModelRule? rule = _rules.FirstOrDefault(r => r.Original == target.Model);
        string substitute = rule?.Substitute ?? target.Model;
        int split = substitute.IndexOf(Separator, StringComparison.Ordinal);
        return Task.FromResult<ModelChoice?>(split < 0
            ? new ModelChoice(substitute, "Factory")
            : new ModelChoice(substitute[..split], substitute[(split + Separator.Length)..]));
    }

    public void SetSubstitute(ModelTarget target, string type, string variation)
    {
        int at = _rules.FindIndex(r => r.Original == target.Model);
        ModelRule rule = new(target.Model, GetReplacement(type, variation), at >= 0 && _rules[at].IsDefault);
        if (at >= 0)
            _rules[at] = rule;
        else
            _rules.Add(rule);
    }

    public void ClearSubstitute(ModelTarget target) => _rules.RemoveAll(r => r.Original == target.Model);

    public string ScanStatus => "";

    public string GetTitle(string type, string variation) => type;

    public ModelChoice? FindChoice(string title) =>
        SampleData.ModelTypes.Contains(title) ? new ModelChoice(title, SampleData.ModelVariations[0]) : null;

    public string? KnownModelsFile() => null;

    public void WriteDebugBundle(string zipPath, string report) => WrittenBundles.Add((zipPath, report));

    /// <summary>The debug bundles that were asked for, so a test can see them.</summary>
    public List<(string Path, string Report)> WrittenBundles { get; } = [];
}

public sealed class FakeVariablesCatalog : IVariablesCatalog
{
    private readonly Dictionary<string, List<string>> _files = SampleData.Variables.ToDictionary(v => v.Model, v => v.Files.ToList());
    private static readonly string[] Defaults = ["Plane.txt", "SingleProp.txt"];

    public int Applied { get; private set; }

    public IReadOnlyList<VariableAssignment> GetAssignments() => [.. _files.Select(f => new VariableAssignment(f.Key, [.. f.Value]))];

    public bool CanPickModel => true;
    public string FilesFolder => Path.Combine(Path.GetTempPath(), "JoinFS", "Variables");

    public IReadOnlyList<string> GetFiles(string model) => _files.TryGetValue(model, out List<string>? files) ? [.. files] : Defaults;

    public void AddFiles(string model, IReadOnlyList<string> files)
    {
        if (!_files.TryGetValue(model, out List<string>? list))
            _files[model] = list = [.. Defaults];
        list.AddRange(files);
    }

    public void RemoveFile(string model, int index)
    {
        if (!_files.TryGetValue(model, out List<string>? list))
            _files[model] = list = [.. Defaults];
        if (index >= 0 && index < list.Count)
            list.RemoveAt(index);
    }

    public bool IsBuiltIn(string file) => Defaults.Contains(file);

    public void Apply() => Applied++;
}

/// <summary>A map without a picture: for tests and for running without a network. The markers still show on the empty ground.</summary>
public sealed class NoMapTiles : IMapTileSource
{
    public string Attribution => "";
    public string AttributionUrl => "";
    public int MaxZoom => 19;
    public Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
}

/// <summary>An app with nothing to say.</summary>
public sealed class NoMessages : IMessageSource
{
    public string? TakeMessage() => null;
}

public sealed class FakeSimBriefClient(TimeSpan latency) : ISimBriefClient
{
    public bool ReportsState => false;
    public ConnectionState State => ConnectionState.Disconnected;
    public void Poll() { }
    public void Reset() { }

    public async Task<FlightPlanData?> FetchAsync(string username, bool commit, CancellationToken cancellationToken)
    {
        await Task.Delay(latency, cancellationToken);
        return new FlightPlanData("HB-TDX", "BE35.0.tt", "VFR", "LSZH", "LSGG", "5500", "LSZH DCT KLO DCT LSGG", "Imported from SimBrief");
    }
}

public sealed class InMemoryFlightPlanStore : IFlightPlanStore
{
    private FlightPlanData _plan = new("HB-TDX", "BE35.0.tt", "VFR", "", "", "", "", "");

    public FlightPlanData Load() => _plan;
    public void Save(FlightPlanData plan) => _plan = plan;
}

public sealed class FakeChatSource : IChatSource
{
    private readonly List<ChatMessage> _messages = [.. SampleData.Chat];

    public IReadOnlyList<ChatMessage> GetMessages() => [.. _messages];

    public bool IsConnected { get; set; } = true;
    public bool CanSend { get; set; } = true;

    public void Send(string text) => _messages.Add(new ChatMessage("You", text, Time: _messages.Count + 1));

    /// <summary>What the others said: a line is added to the chat.</summary>
    public void Say(string from, string text) => _messages.Add(new ChatMessage(from, text, Time: _messages.Count + 1));

    public bool HasUnread { get; private set; } = true;
    public void MarkRead() => HasUnread = false;
}

/// <summary>
/// A recorder that keeps the state the real one does: what is recording, playing and paused, the time, and the length of the take.
/// Time only moves when a test calls <see cref="Advance"/>.
/// </summary>
public sealed class FakeRecorderSource : IRecorderSource
{
    private List<RecordedAircraft> _aircraft;
    private bool _recording, _playing, _paused;
    private double _time, _end;

    /// <param name="empty">Nothing is recorded or loaded.</param>
    public FakeRecorderSource(bool empty = false)
    {
        _aircraft = empty ? [] : [.. SampleData.LoadedRecordList.Select((a, i) => a with { Id = "r" + i })];
        _end = empty ? 0 : 92;
        LoadedRecordingName = empty ? "" : "session_2609.jfs";
    }

    public bool Empty => _aircraft.Count == 0 && _end == 0;

    public RecorderStatus GetStatus() => new(_recording, _playing, _paused, Empty, _playing || _recording ? _time : 0, _end);
    public IReadOnlyList<RecordedAircraft> GetLoadedRecording() => [.. _aircraft];
    public string LoadedRecordingName { get; private set; }
    public bool Loop { get; set; }
    public string RecordingFolder => @"C:\Recordings";

    /// <summary>What was asked for, in order, so a test can see it.</summary>
    public List<string> Calls { get; } = [];

    public void Advance(double seconds)
    {
        if (_paused || !(_playing || _recording))
            return;

        _time += seconds;
        if (_recording)
            _end = Math.Max(_end, _time);
        else if (_time > _end)
        {
            if (Loop)
                _time = 0;
            else
                Stop();
        }
    }

    public void Record()
    {
        Calls.Add("record");
        _recording = _playing = _paused = false;
        _aircraft = [new RecordedAircraft("Me", "Bonanza", "m0")];
        _recording = true;
        _time = 0;
        _end = 0;
        LoadedRecordingName = "";
    }

    public void TogglePlay()
    {
        Calls.Add("play");
        if (_playing)
        {
            _paused = !_paused;
        }
        else if (!Empty && !_recording)
        {
            _playing = true;
            _paused = false;
            _time = 0;
        }
    }

    public void Overdub()
    {
        Calls.Add("overdub");
        if (Empty || _recording)
            return;
        if (!_playing)
        {
            _playing = true;
            _time = 0;
        }
        _recording = true;
    }

    public string AutoSave()
    {
        Calls.Add("autosave");
        if (AutoSaveFails)
            throw new InvalidOperationException("disk full");
        return "2026-10-10_120000_Me.jfs";
    }

    /// <summary>Makes <see cref="AutoSave"/> fail, to test what is done then.</summary>
    public bool AutoSaveFails { get; set; }

    public void Stop()
    {
        Calls.Add("stop");
        _recording = _playing = _paused = false;
        _time = 0;
    }

    public void Seek(double seconds)
    {
        Calls.Add("seek " + seconds);
        _time = seconds;
    }

    public void TrimStart()
    {
        Calls.Add("trim start");
        _end = Math.Max(0, _end - _time);
        _time = 0;
    }

    public void TrimEnd()
    {
        Calls.Add("trim end");
        _end = _time;
    }

    public void SkipAircraft(string aircraftId)
    {
        Calls.Add("skip " + aircraftId);
        _aircraft = [.. _aircraft.Select(a => a.Id == aircraftId ? a with { Skipped = true } : a)];
    }

    public Task OpenAsync(string path, bool append)
    {
        Calls.Add((append ? "add " : "open ") + path);
        if (path.Contains("bad", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This recording is an old version and no longer supported with this version of JoinFS");

        List<RecordedAircraft> loaded = [.. SampleData.LoadedRecordList.Select((a, i) => a with { Id = (append ? "a" : "o") + i })];
        _aircraft = append ? [.. _aircraft, .. loaded] : loaded;
        _end = append ? _end + 92 : 92;
        if (!append)
            LoadedRecordingName = Path.GetFileName(path);
        // as the old window did, a recording that is opened plays
        _recording = false;
        _playing = true;
        _paused = false;
        _time = 0;
        return Task.CompletedTask;
    }

    public Task SaveAsync(string path)
    {
        Calls.Add("save " + path);
        LoadedRecordingName = Path.GetFileName(path);
        return Task.CompletedTask;
    }
}

public sealed class FakeMonitorSource : IMonitorSource
{
    private readonly List<string> _lines = [.. SampleData.LogLines];

    public IReadOnlyList<string> GetLogLines() => [.. _lines];
    public int? FramesPerSecond => 48;

    public bool ShowNetwork { get; set; } = true;
    public bool ShowVariables { get; set; }

    public void WriteNodeStatistics() => _lines.Add("== NODE STATS ==");
    public void WritePacketStatistics() => _lines.Add("== RECEIVED PACKETS ==");

    public IReadOnlyList<string> LogFiles => [];

    /// <summary>What happened in the simulation: a line is added to the log.</summary>
    public void Log(string line) => _lines.Add(line);
}

public sealed class FakeUpdateChecker : IUpdateChecker
{
    public UpdateInfo? CheckForUpdate() => new("26.7.0", "https://github.com/tuduce/JoinFS/releases");
}

public sealed class FakeXPlaneScanSource : IXPlaneScanSource
{
    public string SimFolder => @"C:\X-Plane 12";
    public IReadOnlyList<string> InitialScanFolders => ["Laminar Research"];

    public IReadOnlyList<string> ListAircraftFolders(string xplaneFolder) =>
        string.IsNullOrWhiteSpace(xplaneFolder) ? [] : ["Extra Aircraft", "FlyJSim", "Laminar Research", "Zibo 737"];

    /// <summary>The scans that were asked for, so a test can see them.</summary>
    public List<(string Folder, IReadOnlyList<string> AircraftFolders)> Scans { get; } = [];

    /// <summary>Pretend a scan is already running, so asking for another is refused.</summary>
    public bool Busy { get; set; }

    public bool Scan(string xplaneFolder, IReadOnlyList<string> aircraftFolders)
    {
        if (Busy)
            return false;
        Scans.Add((xplaneFolder, aircraftFolders));
        return true;
    }
}

public sealed class FakeModelScanSource : IModelScanSource
{
    public string SimulatorName => "Microsoft Flight Simulator 2020";
    public string SimFolder => @"C:\Flight Simulator Packages";
    public string FolderPrompt => "Please specify the 'Flight Simulator Packages' folder:";
    public bool ListsSubfolders { get; set; } = true;
    public IReadOnlyList<string> InitialSubfolders => ["Airplanes"];

    public IReadOnlyList<string> ListSubfolders(string simFolder) =>
        string.IsNullOrWhiteSpace(simFolder) ? [] : ["Airplanes", "Boats", "Rotorcraft"];

    public IReadOnlyList<ScanAddOn> AddOns => [new("Aerosoft CRJ", "Aerosoft CRJ", true), new("Fenix A320", "Fenix A320", false)];
    public IReadOnlyList<string> AdditionalFolders => [@"D:\Community"];

    /// <summary>The scans that were asked for, so a test can see them.</summary>
    public List<(string Folder, IReadOnlyList<string> Subfolders, IReadOnlyList<string> AddOns, IReadOnlyList<string> Additional)> Scans { get; } = [];

    /// <summary>Pretend a scan is already running, so asking for another is refused.</summary>
    public bool Busy { get; set; }

    public bool Scan(string simFolder, IReadOnlyList<string> subfolders, IReadOnlyList<string> addOns, IReadOnlyList<string> additionalFolders)
    {
        if (Busy)
            return false;
        Scans.Add((simFolder, subfolders, addOns, additionalFolders));
        return true;
    }
}

public sealed class FakeXPlanePluginInstaller : IXPlanePluginInstaller
{
    public string SavedFolder { get; private set; } = @"C:\X-Plane 12";
    public List<string> Installed { get; } = [];

    public Task InstallAsync(string folder, CancellationToken cancellationToken)
    {
        SavedFolder = folder;
        Installed.Add(folder);
        return Task.CompletedTask;
    }
}

public sealed class FakeAppInfo(bool isXPlaneBuild = false) : IAppInfo
{
    public bool IsXPlaneBuild { get; } = isXPlaneBuild;
    public string Version => "26.6.0";
    // Says so in the title bar, so a run on the fakes cannot be mistaken for the live app.
    public string SessionLabel => "JoinFS-FS2024 (fake data)";
    public string DocumentationUrl => "https://github.com/tuduce/JoinFS/wiki";
    public string DownloadUrl => "https://github.com/tuduce/JoinFS/releases";
    public string Copyright => "© 2026 JoinFS Project. All rights reserved.";
}

public sealed class InMemorySettingsStore(UserSettings? initial = null) : ISettingsStore
{
    private UserSettings _settings = initial ?? new UserSettings();

    public UserSettings Load() => _settings;
    public void Save(UserSettings settings) => _settings = settings;
}

/// <summary>Keeps the preferences, and every version saved, so a test can see what was applied.</summary>
public sealed class InMemoryPreferencesStore(Preferences? initial = null) : IPreferencesStore
{
    private Preferences _current = initial ?? new Preferences();

    public List<Preferences> Saved { get; } = [];

    public Preferences Load() => _current.Clone();

    public void Save(Preferences preferences)
    {
        _current = preferences.Clone();
        Saved.Add(_current);
    }
}

/// <summary>For tests and for running without a window: records what would have happened.</summary>
public sealed class NullPlatform : IPlatform
{
    public List<string> Copied { get; } = [];
    public List<string> OpenedUrls { get; } = [];
    public List<string> OpenedFiles { get; } = [];
    public string? PickedFile { get; set; }
    public string? LastStartFolder { get; private set; }

    public Task CopyTextAsync(string text) { Copied.Add(text); return Task.CompletedTask; }
    public void OpenUrl(string url) => OpenedUrls.Add(url);
    public Task OpenFileAsync(string path) { OpenedFiles.Add(path); return Task.CompletedTask; }
    public Task<string?> PickOpenFileAsync(string title, string? startFolder = null, string? extension = null) { LastStartFolder = startFolder; return Task.FromResult(PickedFile); }
    public string? SavePath { get; set; }
    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string? startFolder = null, string? extension = null) => Task.FromResult(SavePath);
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
}

/// <summary>
/// Shortcuts kept in memory, with the keys of the old Shortcuts window. A test or the previewer "presses" one with <see cref="Press"/>.
/// </summary>
public sealed class FakeShortcutSource : IShortcutSource
{
    private readonly List<ShortcutBinding> _bindings =
    [
        new(ShortcutAction.Network, false, "CTRL+N"),
        new(ShortcutAction.Simulator, false, "CTRL+S"),
        new(ShortcutAction.AllowShared, false, "CTRL+A"),
        new(ShortcutAction.HandOver, false, "CTRL+H"),
        new(ShortcutAction.EnterCockpit, false, "CTRL+E"),
        new(ShortcutAction.Follow, false, "CTRL+F"),
        new(ShortcutAction.Record, false, "CTRL+SHIFT+R"),
        new(ShortcutAction.Overdub, false, "CTRL+SHIFT+O"),
        new(ShortcutAction.Stop, false, "CTRL+SHIFT+X"),
        new(ShortcutAction.Replay, false, "CTRL+SHIFT+P"),
    ];

    private readonly List<ShortcutAction> _pressed = [];

    public IReadOnlyList<ShortcutBinding> Load() => [.. _bindings];

    public void Save(ShortcutBinding binding) => _bindings[(int)binding.Action] = binding;

    public IReadOnlyList<ShortcutAction> TakePressed()
    {
        ShortcutAction[] pressed = [.. _pressed.Where(a => _bindings[(int)a].Enabled)];
        _pressed.Clear();
        return pressed;
    }

    /// <summary>The user presses the shortcut's keys. It counts only if the shortcut is enabled when the keys are looked for.</summary>
    public void Press(ShortcutAction action) => _pressed.Add(action);
}
