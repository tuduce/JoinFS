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

/// <summary>One of the Monitor's "Show (this session)" chips.</summary>
public sealed partial class MonitorFilterViewModel(string key, string label, bool isOn) : ObservableObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    [ObservableProperty]
    private bool _isOn = isOn;

    [RelayCommand]
    private void Toggle() => IsOn = !IsOn;
}

/// <summary>Monitor tab: the log, and which kinds of traffic to show this session.</summary>
public sealed partial class MonitorViewModel : ObservableObject
{
    private readonly IMonitorSource _source;

    public MonitorViewModel(IMonitorSource source)
    {
        _source = source;
        Filters =
        [
            new("nodeStats", "Node Statistics", false),
            new("packets", "Received Packets", false),
            new("network", "Network", true),
            new("variables", "Variables", false),
        ];
        foreach (string line in source.GetLogLines())
            LogLines.Add(line);
    }

    public IReadOnlyList<MonitorFilterViewModel> Filters { get; }

    public ObservableCollection<string> LogLines { get; } = [];

    public string FpsText => $"FPS: {_source.FramesPerSecond}";

    // Opens the log folder. Needs the real log location.
    [RelayCommand]
    private void ViewLogs() { }
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
