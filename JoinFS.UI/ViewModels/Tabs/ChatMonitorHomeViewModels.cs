using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>Chat tab: what is said in the session, and a line to say something. Without a session there is nobody to talk to.</summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IChatSource _source;

    public ChatViewModel(IChatSource source)
    {
        _source = source;
        Refresh();
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ComposerHint))]
    private bool _isConnected;

    /// <summary>What the empty line says: how to chat, or that there is nobody to chat with yet.</summary>
    public string ComposerHint => IsConnected ? Loc.T("Type a message") : Loc.T("Join a hub to chat");

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _draft = "";

    private bool CanSend => !string.IsNullOrWhiteSpace(Draft) && _source.CanSend;

    /// <summary>
    /// Reads the chat again. The lines only ever come at the end and the old ones expire at the start, so what is shown is moved up,
    /// not rebuilt.
    /// </summary>
    public void Refresh()
    {
        IReadOnlyList<ChatMessage> lines = _source.GetMessages();

        // The lines already shown that are still in the new ones: the new ones start with them, after the ones that expired.
        int expired = 0;
        while (expired < Messages.Count && !StartsWith(lines, Messages, expired))
            expired++;

        for (int i = 0; i < expired; i++)
            Messages.RemoveAt(0);
        for (int i = Messages.Count; i < lines.Count; i++)
            Messages.Add(lines[i]);

        IsConnected = _source.IsConnected;
        // Whether a message can be sent now changes with the time, not only with what is typed.
        SendCommand.NotifyCanExecuteChanged();
    }

    // Do the lines shown, from the one at <skip>, start the new lines?
    private static bool StartsWith(IReadOnlyList<ChatMessage> lines, IList<ChatMessage> shown, int skip)
    {
        int count = shown.Count - skip;
        if (count > lines.Count)
            return false;
        for (int i = 0; i < count; i++)
        {
            if (lines[i] != shown[skip + i])
                return false;
        }
        return true;
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        _source.Send(Draft.Trim());
        Draft = "";
        // What was said is among the lines, as it is for everyone.
        Refresh();
    }
}

/// <summary>Monitor tab: the log, what to log this session, and two buttons that write a dump of statistics into it.</summary>
public sealed partial class MonitorViewModel : ObservableObject
{
    private readonly IMonitorSource _source;
    private readonly IPlatform _platform;

    public MonitorViewModel(IMonitorSource source, IPlatform platform)
    {
        _source = source;
        _platform = platform;
        _logNetwork = source.ShowNetwork;
        _logVariables = source.ShowVariables;
        Refresh();
    }

    /// <summary>Whether network traffic goes into the log. A switch: it stays on until it is turned off.</summary>
    [ObservableProperty]
    private bool _logNetwork;

    /// <summary>Whether the variables go into the log. A switch.</summary>
    [ObservableProperty]
    private bool _logVariables;

    partial void OnLogNetworkChanged(bool value) => _source.ShowNetwork = value;

    partial void OnLogVariablesChanged(bool value) => _source.ShowVariables = value;

    /// <summary>Writes the statistics of the nodes into the log, once.</summary>
    [RelayCommand]
    private void WriteNodeStatistics()
    {
        _source.WriteNodeStatistics();
        Refresh();
    }

    /// <summary>Writes the statistics of the received packets into the log, once.</summary>
    [RelayCommand]
    private void WritePacketStatistics()
    {
        _source.WritePacketStatistics();
        Refresh();
    }

    public ObservableCollection<string> LogLines { get; } = [];

    /// <summary>"FPS: 48", or nothing when the frame rate cannot be told.</summary>
    [ObservableProperty]
    private string _fpsText = "";

    /// <summary>Why View Logs did nothing, when it needs saying. Empty otherwise.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>Reads the log and the frame rate again. Lines only ever come at the end, so what is shown is moved up, not rebuilt.</summary>
    public void Refresh()
    {
        IReadOnlyList<string> lines = _source.GetLogLines();

        // The lines already shown that are still in the new ones: the new ones start with them, after the ones that scrolled away.
        int scrolledAway = 0;
        while (scrolledAway < LogLines.Count && !StartsWith(lines, LogLines, scrolledAway))
            scrolledAway++;

        for (int i = 0; i < scrolledAway; i++)
            LogLines.RemoveAt(0);
        for (int i = LogLines.Count; i < lines.Count; i++)
            LogLines.Add(lines[i]);

        FpsText = _source.FramesPerSecond is int fps ? Loc.F("FPS: {0}", fps) : "";
    }

    // Do the lines shown, from the one at <skip>, start the new lines?
    private static bool StartsWith(IReadOnlyList<string> lines, IList<string> shown, int skip)
    {
        int count = shown.Count - skip;
        if (count > lines.Count)
            return false;
        for (int i = 0; i < count; i++)
        {
            if (lines[i] != shown[skip + i])
                return false;
        }
        return true;
    }

    /// <summary>Opens the log files, this run's and the last run's, in whatever program opens text files.</summary>
    [RelayCommand]
    private void ViewLogs()
    {
        IReadOnlyList<string> files = _source.LogFiles;
        if (files.Count == 0)
        {
            Status = Loc.T("There are no log files yet.");
            return;
        }

        Status = "";
        foreach (string file in files)
            _ = _platform.OpenFileAsync(file);
    }
}

/// <summary>Home tab: where you are connected and how busy it is.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly ISessionSource _session;
    private readonly ITrafficSource _traffic;
    private readonly IPlatform _platform;
    private readonly IMapTileProvider _mapTiles;

    public HomeViewModel(MainViewModel main, ISessionSource session, ITrafficSource traffic, IPlatform platform, IMapTileProvider mapTiles)
    {
        _main = main;
        _session = session;
        _traffic = traffic;
        _platform = platform;
        _mapTiles = mapTiles;

        // The two states feed both the greeting and the hub name, so Home follows them live.
        main.Simulator.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Subtitle));
        main.Network.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Subtitle));
        main.AddressBook.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HubName));
        Refresh();
    }

    /// <summary>The aircraft of the Aircraft list that have a position, for the map. Read again by <see cref="Refresh"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMapMarkers))]
    private IReadOnlyList<MapMarker> _mapMarkers = [];

    public bool HasMapMarkers => MapMarkers.Count > 0;

    /// <summary>Which map the pictures are of. Set from Settings → User Interface, and the open map changes at once.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MapTiles), nameof(MapAttribution), nameof(HasMapAttribution))]
    private MapStyle _mapStyle;

    /// <summary>Where the map's pictures come from.</summary>
    public IMapTileSource MapTiles => _mapTiles.Get(MapStyle);

    /// <summary>What the map has to say for the owners of its data. Empty when it has nothing to credit.</summary>
    public string MapAttribution => MapTiles.Attribution;

    public bool HasMapAttribution => MapAttribution.Length > 0;

    /// <summary>Reads the counts and the aircraft on the map again; the live numbers change while the tab is open.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ConnectedUsers));
        OnPropertyChanged(nameof(AircraftTracked));
        MapMarkers = MarkersOf(_traffic.GetAircraft());
    }

    /// <summary>The aircraft that can be put on a map: those with a position, each once.</summary>
    internal static List<MapMarker> MarkersOf(IEnumerable<AircraftInfo> aircraftList)
    {
        List<MapMarker> markers = [];
        HashSet<string> seen = [];
        foreach (AircraftInfo aircraft in aircraftList)
        {
            // no position yet, or the 0, 0 an aircraft has before its first one arrives
            if (aircraft.Latitude is not { } latitude || aircraft.Longitude is not { } longitude || (latitude == 0 && longitude == 0))
                continue;
            if (!seen.Add(aircraft.Id))
                continue;
            markers.Add(new MapMarker(aircraft.Id, aircraft.Callsign, latitude, longitude, aircraft.Heading ?? 0));
        }
        return markers;
    }

    [RelayCommand]
    private void OpenMapAttribution()
    {
        if (MapTiles.AttributionUrl.Length > 0)
            _platform.OpenUrl(MapTiles.AttributionUrl);
    }

    public string HubName => _main.AddressBook.TransientLabel ?? _main.AddressBook.EffectiveSelection?.Name ?? "—";
    public int ConnectedUsers => _session.GetPeers().Count;
    public int AircraftTracked => _traffic.GetAircraft().Count;

    public string Subtitle => (_main.Simulator.IsConnected, _main.Network.IsConnected) switch
    {
        (true, true) => Loc.T("Simulator and network are both connected.") + " " + Loc.T("Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching."),
        (false, false) => Loc.T("Connect the simulator and join a hub to start flying together.") + " " + Loc.T("Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching."),
        (true, false) => Loc.T("The simulator is connected. Join a hub to see other pilots.") + " " + Loc.T("Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching."),
        (false, true) => Loc.T("The network is connected. Connect the simulator to fly with them.") + " " + Loc.T("Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching."),
    };
}
