using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>Chat tab: the message list and a composer. The README flags the look for a later visual pass.</summary>
public sealed partial class ChatViewModel : ObservableObject
{
    private readonly IChatSource _source;

    public ChatViewModel(IChatSource source)
    {
        _source = source;
        foreach (ChatMessage message in source.GetMessages())
            Messages.Add(message);
    }

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _draft = "";

    private bool CanSend => !string.IsNullOrWhiteSpace(Draft);

    [RelayCommand(CanExecute = nameof(CanSend))]
    private void Send()
    {
        string text = Draft.Trim();
        _source.Send(text);
        Messages.Add(new ChatMessage("You", text));
        Draft = "";
    }
}

/// <summary>
/// One of the Monitor's "Show (this session)" chips. A chip that shows something is a switch: it stays on while it does. A one-shot chip
/// (a dump of statistics into the log) is a button: it does its work when it is pressed and has no state.
/// </summary>
public sealed partial class MonitorFilterViewModel(string key, string label, bool isOn, Action<bool>? changed = null, bool oneShot = false) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    /// <summary>A button that does something once, not a switch.</summary>
    public bool IsOneShot { get; } = oneShot;

    [ObservableProperty]
    private bool _isOn = isOn;

    partial void OnIsOnChanged(bool value)
    {
        if (!IsOneShot)
            changed?.Invoke(value);
    }

    /// <summary>Flips a switch; does a one-shot chip's work.</summary>
    [RelayCommand]
    private void Toggle()
    {
        if (IsOneShot)
            changed?.Invoke(true);
        else
            IsOn = !IsOn;
    }
}

/// <summary>Monitor tab: the log, and which kinds of traffic to show this session.</summary>
public sealed partial class MonitorViewModel : ObservableObject
{
    private readonly IMonitorSource _source;
    private readonly IPlatform _platform;

    public MonitorViewModel(IMonitorSource source, IPlatform platform)
    {
        _source = source;
        _platform = platform;
        Filters =
        [
            new("nodeStats", "Node Statistics", false, _ => { source.WriteNodeStatistics(); Refresh(); }, oneShot: true),
            new("packets", "Received Packets", false, _ => { source.WritePacketStatistics(); Refresh(); }, oneShot: true),
            new("network", "Network", source.ShowNetwork, on => source.ShowNetwork = on),
            new("variables", "Variables", source.ShowVariables, on => source.ShowVariables = on),
        ];
        Refresh();
    }

    public IReadOnlyList<MonitorFilterViewModel> Filters { get; }

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

        FpsText = _source.FramesPerSecond is int fps ? $"FPS: {fps}" : "";
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
            Status = "There are no log files yet.";
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
    private readonly IAppInfo _app;
    private readonly IPlatform _platform;

    public HomeViewModel(MainViewModel main, ISessionSource session, ITrafficSource traffic, IAppInfo app, IPlatform platform)
    {
        _main = main;
        _session = session;
        _traffic = traffic;
        _app = app;
        _platform = platform;

        // The two states feed both the greeting and the hub name, so Home follows them live.
        main.Simulator.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Subtitle));
        main.Network.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Subtitle));
        main.AddressBook.PropertyChanged += (_, _) => OnPropertyChanged(nameof(HubName));
    }

    /// <summary>Reads the counts again; the live numbers change while the tab is open.</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(ConnectedUsers));
        OnPropertyChanged(nameof(AircraftTracked));
    }

    public string HubName => _main.AddressBook.TransientLabel ?? _main.AddressBook.EffectiveSelection?.Name ?? "—";
    public int ConnectedUsers => _session.GetPeers().Count;
    public int AircraftTracked => _traffic.GetAircraft().Count;

    public string Subtitle => (_main.Simulator.IsConnected, _main.Network.IsConnected) switch
    {
        (true, true) => "Simulator and network are both connected. Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching.",
        (false, false) => "Connect the simulator and join a hub to start flying together. Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching.",
        (true, false) => "The simulator is connected. Join a hub to see other pilots. Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching.",
        (false, true) => "The network is connected. Connect the simulator to fly with them. Pick a panel from the sidebar to manage hubs, session traffic, aircraft, or model matching.",
    };

    [RelayCommand]
    private void OpenDownload() => _platform.OpenUrl(_app.DownloadUrl);
}
