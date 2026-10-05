using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// One hub in the Network Hubs table. The row lives as long as the hub is listed and is updated in place on each refresh, so an open row
/// survives it. Ignoring and saving go straight to the service.
/// </summary>
public sealed partial class HubRowViewModel : ObservableObject
{
    private readonly HubsViewModel _owner;

    internal HubRowViewModel(HubInfo hub, HubsViewModel owner)
    {
        Hub = hub;
        _owner = owner;
        _isIgnored = hub.Ignored;
        _isSaved = hub.Saved;
    }

    public HubInfo Hub { get; private set; }
    public string Id => Hub.Id;
    public string Name => Hub.Name;
    public string Status => Hub.Status.ToString();
    public int Users => Hub.Users;
    public int Aircraft => Hub.Aircraft;
    public string Version => Hub.Version;
    public string About => Hub.About;
    public string Voice => Hub.Voice;
    public string NextEvent => Hub.NextEvent;

    // One flag per status, so the view can pick the badge colour with a style class.
    public bool IsOnline => Hub.Status == HubStatus.Online;
    public bool IsGlobal => Hub.Status == HubStatus.Global;
    public bool IsPassword => Hub.Status == HubStatus.Password;
    public bool IsOffline => Hub.Status == HubStatus.Offline;

    /// <summary>False for an offline hub, and for this node's own hub (in hub mode), which has no address to join.</summary>
    public bool CanJoin => Hub.CanJoin;

    public bool CanIgnore => Hub.CanIgnore;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreLabel))]
    private bool _isIgnored;

    public string IgnoreLabel => IsIgnored ? "Unignore" : "Ignore";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SaveLabel))]
    private bool _isSaved;

    public string SaveLabel => IsSaved ? "Remove From Address Book" : "Add to Address Book";

    /// <summary>Takes a newer reading of the same hub.</summary>
    internal void Update(HubInfo hub)
    {
        Hub = hub;
        OnPropertyChanged(string.Empty); // every display property may have changed
        IsIgnored = hub.Ignored;
        IsSaved = hub.Saved;
    }

    [RelayCommand]
    private void ToggleExpanded() => _owner.Expand(this);

    /// <summary>True while the network is connected to this hub: the Join link then reads Leave.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinLabel))]
    [NotifyCanExecuteChangedFor(nameof(JoinCommand))]
    private bool _isJoined;

    public string JoinLabel => IsJoined ? "Leave" : "Join";

    // Leaving is always possible, even for a hub that could not be joined (it went offline while we were in it).
    private bool CanJoinOrLeave() => IsJoined || CanJoin;

    [RelayCommand(CanExecute = nameof(CanJoinOrLeave))]
    private Task JoinAsync() => IsJoined ? _owner.LeaveAsync() : _owner.JoinAsync(this);

    [RelayCommand]
    private void ToggleSave() => _owner.SetSaved(this, !IsSaved);

    [RelayCommand(CanExecute = nameof(CanIgnore))]
    private void ToggleIgnore() => _owner.SetIgnored(this, !IsIgnored);
}

/// <summary>Network Hubs tab: the hub directory, plus "create your own mesh".</summary>
public sealed partial class HubsViewModel : ObservableObject
{
    private readonly IHubDirectory _directory;
    private readonly INetworkLink _network;
    private readonly IShell _shell;
    private readonly SortController<HubRowViewModel> _sort;
    private readonly Dictionary<string, HubRowViewModel> _rowsById = [];
    private List<HubRowViewModel> _all = [];

    public HubsViewModel(IHubDirectory directory, INetworkLink network, IShell shell)
    {
        _directory = directory;
        _network = network;
        _shell = shell;
        _meshCode = network.MeshCode;

        _sort = new SortController<HubRowViewModel>(Rebuild, initialKey: "name");
        NameColumn = _sort.Add("name", "Name", r => r.Name);
        StatusColumn = _sort.Add("status", "Status", r => r.Status);
        UsersColumn = _sort.Add("users", "Users", r => r.Users);
        AircraftColumn = _sort.Add("aircraft", "Aircraft", r => r.Aircraft, highestFirst: true);
        VersionColumn = _sort.Add("version", "Version", r => new VersionKey(r.Version), highestFirst: true);

        Refresh();
    }

    /// <summary>Raised after Add to / Remove From Address Book changed the book, so whoever shows it can read it again.</summary>
    public event EventHandler? AddressBookChanged;

    public SortColumn NameColumn { get; }
    public SortColumn StatusColumn { get; }
    public SortColumn UsersColumn { get; }
    public SortColumn AircraftColumn { get; }
    public SortColumn VersionColumn { get; }

    /// <summary>The rows shown: filtered by <see cref="ShowOfflineHubs"/> and sorted.</summary>
    public ObservableCollection<HubRowViewModel> Rows { get; } = [];

    /// <summary>The "(53)" after "Public Hubs": every hub the directory knows, whether or not it is shown.</summary>
    public int HubCount => _all.Count;

    [ObservableProperty]
    private bool _showOfflineHubs;

    partial void OnShowOfflineHubsChanged(bool value) => Rebuild();

    [ObservableProperty]
    private string _meshCode;

    /// <summary>Reads the directory again. Rows of hubs still there are updated in place; new hubs get a row, hubs gone lose theirs.</summary>
    [RelayCommand]
    public void Refresh()
    {
        _all = [];
        HashSet<string> seen = [];
        foreach (HubInfo hub in _directory.GetHubs())
        {
            if (!seen.Add(hub.Id))
                continue; // an id names one hub; a repeat is a fault in the source and would break the list
            if (_rowsById.TryGetValue(hub.Id, out HubRowViewModel? row))
                row.Update(hub);
            else
                _rowsById[hub.Id] = row = new HubRowViewModel(hub, this);
            _all.Add(row);
        }

        foreach (string gone in _rowsById.Keys.Except(seen).ToList())
            _rowsById.Remove(gone);

        OnPropertyChanged(nameof(HubCount));
        SyncJoined();
        Rebuild();
        MeshCode = _network.MeshCode;
    }

    [RelayCommand]
    private async Task CreateMeshAsync()
    {
        await _shell.CreateMeshAsync();
        MeshCode = _network.MeshCode;
    }

    /// <summary>One row open at a time, accordion style. Clicking the open row closes it.</summary>
    internal void Expand(HubRowViewModel row)
    {
        bool open = !row.IsExpanded;
        foreach (HubRowViewModel other in _rowsById.Values)
            other.IsExpanded = false;
        row.IsExpanded = open;
    }

    internal Task JoinAsync(HubRowViewModel row)
    {
        AddressBookEntry target = new(row.Hub.Name, row.Hub.Address, RequiresPassword: row.Hub.Status == HubStatus.Password);
        return _shell.JoinAsync(target);
    }

    internal Task LeaveAsync() => _shell.LeaveAsync();

    /// <summary>Marks the row of the hub the network is connected to, so its Join link reads Leave.</summary>
    public void SyncJoined()
    {
        string? joined = _shell.JoinedHubName;
        foreach (HubRowViewModel row in _rowsById.Values)
            row.IsJoined = joined is not null && string.Equals(row.Name, joined, StringComparison.OrdinalIgnoreCase);
    }

    internal void SetSaved(HubRowViewModel row, bool saved)
    {
        _directory.SetSaved(row.Id, saved);
        row.IsSaved = saved;
        AddressBookChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetIgnored(HubRowViewModel row, bool ignored)
    {
        _directory.SetIgnored(row.Id, ignored);
        row.IsIgnored = ignored;
    }

    private void Rebuild()
    {
        IEnumerable<HubRowViewModel> visible = _all.Where(r => ShowOfflineHubs || r.Hub.Status != HubStatus.Offline);
        CollectionSync.Reconcile(Rows, [.. _sort.Apply(visible)]);
    }
}
