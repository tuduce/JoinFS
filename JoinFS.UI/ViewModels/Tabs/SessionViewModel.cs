using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// One user in the Session table. The row lives as long as the user is connected and is updated in place, so an open row, a
/// half-ticked box and the scroll position survive every refresh. What the user sets on it goes straight to the service.
/// </summary>
public sealed partial class PeerRowViewModel : ObservableObject
{
    private readonly SessionViewModel _owner;

    // True while the row is being filled from the service, so what it reads is not written back as if the user had set it.
    private bool _syncing;

    internal PeerRowViewModel(PeerInfo peer, PeerSettings settings, SessionViewModel owner)
    {
        Peer = peer;
        _owner = owner;
        Apply(settings);
    }

    /// <summary>The keys of the shortcuts, for the tooltips of the permissions.</summary>
    public ShortcutHints Hints => _owner.Hints;

    public PeerInfo Peer { get; private set; }
    public string Id => Peer.Id;
    public string Nick => Peer.Nick;
    public string Callsign => Peer.Callsign;
    // The peer says "Yes", "Route" or "No" (see PeerInfo); the table shows them in the language.
    public string Connected => Peer.Connected switch { "Yes" => Loc.T("Yes"), "Route" => Loc.T("Route"), "No" => Loc.T("No"), var other => other };
    public int Latency => Peer.LatencyMs;
    public string Simulator => Peer.Simulator;
    public string Version => Peer.Version;
    public string Protocol => Peer.Protocol == "Pending" ? Loc.T("Pending") : Peer.Protocol;

    /// <summary>Released builds only speak the legacy wire, so the badge calls it out.</summary>
    public bool IsLegacy => Peer.IsLegacy;

    public int Aircraft => Peer.Aircraft;
    public int ObjectsExported => Peer.Objects;
    public int Port => Peer.Port;

    /// <summary>Everyone but this node: you cannot save, ignore or give permissions to yourself.</summary>
    public bool IsOther => !Peer.IsMe;

    /// <summary>Takes a newer reading of the same user.</summary>
    internal void Update(PeerInfo peer, PeerSettings settings)
    {
        Peer = peer;
        OnPropertyChanged(string.Empty); // every display property may have changed
        Apply(settings);
    }

    private void Apply(PeerSettings settings)
    {
        _syncing = true;
        try
        {
            CockpitEntry = settings.CockpitEntry;
            _handOverRequested = settings.HandOverControls;
            OnPropertyChanged(nameof(HandOverControls));
            MultipleObjects = settings.MultipleObjects;
            IsSaved = settings.IsSaved;
            IsIgnored = settings.IsIgnored;
        }
        finally
        {
            _syncing = false;
        }
    }

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreLabel))]
    private bool _isIgnored;

    public string IgnoreLabel => IsIgnored ? Loc.T("Unignore") : Loc.T("Ignore");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HandOverControls), nameof(CanHandOverControls))]
    private bool _cockpitEntry;

    [ObservableProperty]
    private bool _multipleObjects;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    private bool _isSaved;

    public string SaveLabel => IsSaved ? Loc.T("Remove From Address Book") : Loc.T("Save");

    private bool _handOverRequested;

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
            if (!_syncing)
                _owner.Source.SetHandOverControls(Id, value);
        }
    }

    partial void OnCockpitEntryChanged(bool value)
    {
        if (!_syncing)
            _owner.Source.SetCockpitEntry(Id, value);
    }

    partial void OnMultipleObjectsChanged(bool value)
    {
        if (!_syncing)
            _owner.Source.SetMultipleObjects(Id, value);
    }

    [RelayCommand]
    private void ToggleExpanded() => _owner.Expand(this);

    [RelayCommand]
    private void ToggleSave()
    {
        _owner.Source.SetSaved(Id, !IsSaved);
        IsSaved = _owner.Source.GetSettings(Id).IsSaved;
        _owner.OnAddressBookChanged();
    }

    [RelayCommand]
    private void ToggleIgnore()
    {
        _owner.Source.SetIgnored(Id, !IsIgnored);
        IsIgnored = _owner.Source.GetSettings(Id).IsIgnored;
    }
}

/// <summary>Session tab: the users connected to the network, with per-user settings in the expanded row.</summary>
public sealed partial class SessionViewModel : ObservableObject
{
    private readonly Dictionary<string, PeerRowViewModel> _rowsById = [];

    public SessionViewModel(ISessionSource source, ShortcutHints hints)
    {
        Source = source;
        Hints = hints;
        Refresh();
    }

    internal ISessionSource Source { get; }

    /// <summary>The keys of the shortcuts, for the rows' tooltips.</summary>
    public ShortcutHints Hints { get; }

    /// <summary>Raised after Save added or removed an address book entry, so whoever shows the address book can read it again.</summary>
    public event EventHandler? AddressBookChanged;

    public ObservableCollection<PeerRowViewModel> Rows { get; } = [];

    public int PeerCount => Rows.Count;

    /// <summary>Reads the session again. Rows of users still there are updated in place; new users get a row, users gone lose theirs.</summary>
    [RelayCommand]
    public void Refresh()
    {
        IReadOnlyList<PeerInfo> peers = Source.GetPeers();

        List<PeerRowViewModel> wanted = [];
        HashSet<string> seen = [];
        foreach (PeerInfo peer in peers)
        {
            if (!seen.Add(peer.Id))
                continue; // an id names one user; a repeat is a fault in the source and would break the list
            PeerSettings settings = peer.IsMe ? new PeerSettings(false, false, false, false, false) : Source.GetSettings(peer.Id);
            if (_rowsById.TryGetValue(peer.Id, out PeerRowViewModel? row))
                row.Update(peer, settings);
            else
                _rowsById[peer.Id] = row = new PeerRowViewModel(peer, settings, this);
            wanted.Add(row);
        }

        foreach (string gone in _rowsById.Keys.Except(wanted.Select(r => r.Id)).ToList())
            _rowsById.Remove(gone);

        CollectionSync.Reconcile(Rows, wanted);

        OnPropertyChanged(nameof(PeerCount));
    }

    /// <summary>The user whose row is open, who is not you. Null when no row is open: the shortcuts have no one to act on.</summary>
    private PeerRowViewModel? OpenOtherRow()
    {
        Refresh(); // the tab is not refreshed while it is hidden, and a shortcut toggles what is true now
        return Rows.FirstOrDefault(r => r.IsExpanded && r.IsOther);
    }

    /// <summary>The shortcut that allows or stops cockpit entry for the user whose row is open.</summary>
    public void ToggleCockpitEntryOfOpenRow()
    {
        if (OpenOtherRow() is { } row)
            row.CockpitEntry = !row.CockpitEntry;
    }

    /// <summary>The shortcut that hands the flight controls to the user whose row is open, or takes them back.</summary>
    public void ToggleHandOverOfOpenRow()
    {
        if (OpenOtherRow() is { } row)
            row.HandOverControls = !row.HandOverControls;
    }

    internal void Expand(PeerRowViewModel row)
    {
        bool open = !row.IsExpanded;
        foreach (PeerRowViewModel other in Rows)
            other.IsExpanded = false;
        row.IsExpanded = open;
    }

    internal void OnAddressBookChanged() => AddressBookChanged?.Invoke(this, EventArgs.Empty);
}
