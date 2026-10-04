using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

public sealed partial class HubRowViewModel : ObservableObject
{
    private readonly HubsViewModel _owner;

    internal HubRowViewModel(HubInfo hub, HubsViewModel owner, bool isIgnored)
    {
        Hub = hub;
        _owner = owner;
        _isIgnored = isIgnored;
    }

    public HubInfo Hub { get; }
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

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreLabel))]
    private bool _isIgnored;

    public string IgnoreLabel => IsIgnored ? "Unignore" : "Ignore";

    [RelayCommand]
    private void ToggleExpanded() => _owner.Expand(this);

    [RelayCommand]
    private Task JoinAsync() => _owner.JoinAsync(this);

    [RelayCommand]
    private void AddToAddressBook() => _owner.AddToAddressBook(this);

    [RelayCommand]
    private void ToggleIgnore() => IsIgnored = !IsIgnored;
}

/// <summary>Network Hubs tab: the public hub directory, plus "create your own mesh".</summary>
public sealed partial class HubsViewModel : ObservableObject
{
    private readonly IHubDirectory _directory;
    private readonly INetworkLink _network;
    private readonly AddressBookViewModel _addressBook;
    private readonly IShell _shell;
    private readonly SortController<HubRowViewModel> _sort;
    private readonly Dictionary<string, HubRowViewModel> _rowsByName = [];
    private readonly HashSet<string> _ignoredAtStart;
    private List<HubInfo> _hubs = [];

    /// <param name="ignoredHubs">Hubs struck through from the start. The prototype ignores NoiseAbatement Hub.</param>
    public HubsViewModel(IHubDirectory directory, INetworkLink network, AddressBookViewModel addressBook, IShell shell, IEnumerable<string>? ignoredHubs = null)
    {
        _directory = directory;
        _network = network;
        _addressBook = addressBook;
        _shell = shell;
        _ignoredAtStart = [.. ignoredHubs ?? []];
        _meshCode = network.MeshCode;

        _sort = new SortController<HubRowViewModel>(Rebuild, initialKey: "name");
        NameColumn = _sort.Add("name", "Name", r => r.Name);
        StatusColumn = _sort.Add("status", "Status", r => r.Status);
        UsersColumn = _sort.Add("users", "Users", r => r.Users);
        AircraftColumn = _sort.Add("aircraft", "Aircraft", r => r.Aircraft);
        VersionColumn = _sort.Add("version", "Version", r => r.Version);
    }

    public SortColumn NameColumn { get; }
    public SortColumn StatusColumn { get; }
    public SortColumn UsersColumn { get; }
    public SortColumn AircraftColumn { get; }
    public SortColumn VersionColumn { get; }

    /// <summary>The rows shown: filtered by <see cref="ShowOfflineHubs"/> and sorted.</summary>
    public ObservableCollection<HubRowViewModel> Rows { get; } = [];

    /// <summary>The "(53)" after "Public Hubs": every hub the directory knows, whether or not it is shown.</summary>
    public int HubCount => _hubs.Count;

    [ObservableProperty]
    private bool _showOfflineHubs;

    partial void OnShowOfflineHubsChanged(bool value) => Rebuild();

    [ObservableProperty]
    private string _meshCode;

    public async Task RefreshAsync()
    {
        _hubs = [.. await _directory.GetPublicHubsAsync(CancellationToken.None)];

        // Rows outlive a refresh, so what the user did (expanded, ignored) survives it.
        foreach (HubInfo hub in _hubs)
            if (!_rowsByName.ContainsKey(hub.Name))
                _rowsByName[hub.Name] = new HubRowViewModel(hub, this, _ignoredAtStart.Contains(hub.Name));

        OnPropertyChanged(nameof(HubCount));
        Rebuild();
    }

    [RelayCommand]
    private Task Refresh() => RefreshAsync();

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
        foreach (HubRowViewModel other in _rowsByName.Values)
            other.IsExpanded = false;
        row.IsExpanded = open;
    }

    internal Task JoinAsync(HubRowViewModel row)
    {
        AddressBookEntry target = new(row.Hub.Name, row.Hub.Address, RequiresPassword: row.Hub.Status == HubStatus.Password);
        return _shell.JoinAsync(target);
    }

    internal void AddToAddressBook(HubRowViewModel row) => _addressBook.AddHub(row.Hub);

    private void Rebuild()
    {
        IEnumerable<HubRowViewModel> visible = _hubs
            .Select(h => _rowsByName[h.Name])
            .Where(r => ShowOfflineHubs || r.Hub.Status != HubStatus.Offline);

        Rows.Clear();
        foreach (HubRowViewModel row in _sort.Apply(visible))
            Rows.Add(row);
    }
}
