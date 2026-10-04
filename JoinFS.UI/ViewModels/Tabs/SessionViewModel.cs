using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

public sealed partial class PeerRowViewModel : ObservableObject
{
    private readonly SessionViewModel _owner;
    private bool _handOverRequested;

    internal PeerRowViewModel(PeerInfo peer, SessionViewModel owner)
    {
        Peer = peer;
        _owner = owner;
    }

    public PeerInfo Peer { get; }
    public string Nick => Peer.Nick;
    public string Callsign => Peer.Callsign;
    public string Connected => Peer.Connected ? "Yes" : "No";
    public int Latency => Peer.LatencyMs;
    public string Simulator => Peer.Simulator;
    public string Version => Peer.Version;
    public string Protocol => Peer.Protocol;

    /// <summary>Released builds only speak the legacy wire, so the badge calls it out.</summary>
    public bool IsLegacy => Peer.IsLegacy;

    public int Aircraft => Peer.Aircraft;
    public int Port => Peer.Port;
    public int ObjectsExported => Peer.Aircraft + (MultipleObjects ? Peer.Aircraft : 0);

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreLabel))]
    private bool _isIgnored;

    public string IgnoreLabel => IsIgnored ? "Unignore" : "Ignore";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandOverControls), nameof(CanHandOverControls))]
    private bool _cockpitEntry;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ObjectsExported))]
    private bool _multipleObjects;

    /// <summary>Handing over controls only makes sense with cockpit entry; the box is off and disabled without it.</summary>
    public bool CanHandOverControls => CockpitEntry;

    public bool HandOverControls
    {
        get => CockpitEntry && _handOverRequested;
        set
        {
            if (!CockpitEntry || _handOverRequested == value)
                return;
            _handOverRequested = value;
            OnPropertyChanged();
        }
    }

    [ObservableProperty]
    private bool _isSaved;

    [RelayCommand]
    private void ToggleExpanded() => _owner.Expand(this);

    [RelayCommand]
    private void Save() => IsSaved = true;

    [RelayCommand]
    private void ToggleIgnore() => IsIgnored = !IsIgnored;
}

/// <summary>Session tab: the users connected to the network, with per-user settings in the expanded row.</summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly ISessionSource _source;
    private readonly Dictionary<string, PeerRowViewModel> _rowsByNick = [];

    public SessionViewModel(ISessionSource source, IEnumerable<string>? ignoredPeers = null)
    {
        _source = source;
        _ignoredAtStart = [.. ignoredPeers ?? []];
        Refresh();
    }

    private readonly HashSet<string> _ignoredAtStart;

    public ObservableCollection<PeerRowViewModel> Rows { get; } = [];

    public int PeerCount => Rows.Count;

    [RelayCommand]
    public void Refresh()
    {
        Rows.Clear();
        foreach (PeerInfo peer in _source.GetPeers())
        {
            // A row outlives a refresh, so a user's settings are not lost when the list reloads.
            if (!_rowsByNick.TryGetValue(peer.Nick, out PeerRowViewModel? row))
                _rowsByNick[peer.Nick] = row = new PeerRowViewModel(peer, this) { IsIgnored = _ignoredAtStart.Contains(peer.Nick) };
            Rows.Add(row);
        }
        OnPropertyChanged(nameof(PeerCount));
    }

    internal void Expand(PeerRowViewModel row)
    {
        bool open = !row.IsExpanded;
        foreach (PeerRowViewModel other in Rows)
            other.IsExpanded = false;
        row.IsExpanded = open;
    }
}
