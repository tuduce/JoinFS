using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// One row of the Objects table. It lives as long as its object (or group) is listed and is updated in place on each refresh. What the user
/// ticks goes straight to the service; the broadcast links are the old Broadcast dialog's four checkboxes.
/// </summary>
public sealed partial class ObjectRowViewModel : ObservableObject
{
    private readonly ObjectsViewModel _owner;
    private readonly ActionLink _substitute, _thisObject, _model, _tacpack, _everything;

    // True while the row is being filled from the service, so what it reads is not written back as if the user had set it.
    private bool _syncing;

    internal ObjectRowViewModel(ObjectInfo info, ObjectsViewModel owner)
    {
        Info = info;
        _owner = owner;

        _substitute = new("Substitute…", new RelayCommand(() => _ = _owner.SubstituteAsync(this), () => Info.CanSubstitute));
        _thisObject = new("", new RelayCommand(() => Broadcast = !Broadcast, () => Info.CanBroadcast && !IsGroup));
        _model = new("", new RelayCommand(() => _owner.Source.SetModelBroadcast(Info.OriginalModel, !Info.ModelBroadcast), () => Info.CanBroadcast));
        _tacpack = new("", new RelayCommand(() => _owner.Profile.BroadcastTacpack = !_owner.Profile.BroadcastTacpack));
        _everything = new("", new RelayCommand(() => _owner.Profile.BroadcastEverything = !_owner.Profile.BroadcastEverything));
        Actions = [_substitute, _thisObject, _model, _tacpack, _everything];

        Apply(info);
    }

    public ObjectInfo Info { get; private set; }
    public string Id => Info.Id;
    public string Owner => Info.Owner;
    public string Model => Info.Model;
    public int Count => Info.Count;

    // What the columns sort by: an unknown value before every known one.
    public int BearingValue => Info.Bearing ?? -1;
    public double DistanceValue => Info.DistanceNm ?? -1;

    public string Bearing => Info.Bearing is { } b ? b.ToString("D3", CultureInfo.InvariantCulture) : "-";
    public string Distance => Info.DistanceNm is { } d ? $"{d.ToString("N1", CultureInfo.CurrentCulture)} nm" : "-";

    /// <summary>A row that stands for all the objects of one owner and model: every row, while "Group by model" is on.</summary>
    public bool IsGroup => _owner.GroupByModel;

    public bool CanBroadcast => Info.CanBroadcast;
    public bool CanIgnore => Info.CanIgnore;

    [ObservableProperty]
    private bool _broadcast;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIgnored))]
    private bool _ignoreOwner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIgnored))]
    private bool _ignoreModel;

    /// <summary>Ignored rows are drawn muted, and hidden unless "List ignored objects" is on.</summary>
    public bool IsIgnored => IgnoreOwner || IgnoreModel;

    /// <summary>The selected row is the open one.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>The links of the expanded row's Actions block.</summary>
    public IReadOnlyList<ActionLink> Actions { get; }

    [RelayCommand]
    private void Select() => _owner.Select(this);

    // A group's box is its model's broadcast; a single object's box is that object's.
    partial void OnBroadcastChanged(bool value)
    {
        if (_syncing)
            return;
        if (Info.CanBroadcast)
        {
            if (IsGroup)
                _owner.Source.SetModelBroadcast(Info.OriginalModel, value);
            else
                _owner.Source.SetObjectBroadcast(Id, value);
        }
        RefreshLinks();
    }

    partial void OnIgnoreOwnerChanged(bool value)
    {
        if (_syncing)
            return;
        if (Info.CanIgnore)
            _owner.Source.SetIgnoreOwner(Id, value);
        _owner.OnIgnoreChanged();
    }

    partial void OnIgnoreModelChanged(bool value)
    {
        if (_syncing)
            return;
        if (Info.CanIgnore)
            _owner.Source.SetIgnoreModel(Id, value);
        _owner.OnIgnoreChanged();
    }

    /// <summary>Takes a newer reading of the same object.</summary>
    internal void Update(ObjectInfo info)
    {
        Info = info;
        OnPropertyChanged(string.Empty); // every display property may have changed
        Apply(info);
    }

    private void Apply(ObjectInfo info)
    {
        _syncing = true;
        try
        {
            Broadcast = info.Broadcast;
            IgnoreOwner = info.IgnoreOwner;
            IgnoreModel = info.IgnoreModel;
        }
        finally
        {
            _syncing = false;
        }
        RefreshLinks();
    }

    /// <summary>Words each broadcast link by its current state, and re-asks whether it is available.</summary>
    internal void RefreshLinks()
    {
        _thisObject.Label = Broadcast ? "Stop Broadcasting This Object" : "Broadcast This Object";
        _model.Label = Info.ModelBroadcast ? $"Stop Broadcasting All '{Info.OriginalModel}'" : $"Broadcast All '{Info.OriginalModel}'";
        _tacpack.Label = _owner.Profile.BroadcastTacpack ? "Stop Broadcasting VRS TacPack" : "Broadcast VRS TacPack";
        _everything.Label = _owner.Profile.BroadcastEverything ? "Stop Broadcasting Everything" : "Broadcast Everything";

        foreach (ActionLink link in Actions)
            (link.Command as RelayCommand)?.NotifyCanExecuteChanged();
    }
}

/// <summary>Objects tab: scenery and shared objects, laid out like the Aircraft table, with broadcasting in the expanded row.</summary>
public sealed partial class ObjectsViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private readonly IShell _shell;
    private readonly SortController<ObjectRowViewModel> _sort;
    private readonly Dictionary<string, ObjectRowViewModel> _rowsById = [];
    private List<ObjectRowViewModel> _inSourceOrder = [];
    private bool _syncingGroup;

    public ObjectsViewModel(ITrafficSource traffic, IModelCatalog catalog, ProfileViewModel profile, IShell shell)
    {
        Source = traffic;
        _catalog = catalog;
        Profile = profile;
        _shell = shell;
        _syncingGroup = true;
        _groupByModel = traffic.GroupObjects;
        _syncingGroup = false;

        _sort = new SortController<ObjectRowViewModel>(Rebuild);
        OwnerColumn = _sort.Add("owner", "Owner", r => r.Owner);
        ModelColumn = _sort.Add("model", "Model", r => r.Model);
        CountColumn = _sort.Add("count", "Count", r => r.Count);
        BearingColumn = _sort.Add("bearing", "Bearing", r => r.BearingValue);
        DistanceColumn = _sort.Add("distance", "Distance", r => r.DistanceValue);

        // The TacPack and Everything links read the profile, which Settings → Simulator also edits.
        profile.PropertyChanged += OnProfileChanged;

        Refresh();
    }

    internal ITrafficSource Source { get; }
    public ProfileViewModel Profile { get; }

    public SortColumn OwnerColumn { get; }
    public SortColumn ModelColumn { get; }
    public SortColumn CountColumn { get; }
    public SortColumn BearingColumn { get; }
    public SortColumn DistanceColumn { get; }

    public ObservableCollection<ObjectRowViewModel> Rows { get; } = [];

    /// <summary>Merge the objects of one owner and model into one row with a count. The old window's "group objects" setting.</summary>
    [ObservableProperty]
    private bool _groupByModel;

    partial void OnGroupByModelChanged(bool value)
    {
        if (_syncingGroup)
            return;
        Source.GroupObjects = value;
        Refresh();
    }

    [ObservableProperty]
    private bool _listIgnoredObjects;

    partial void OnListIgnoredObjectsChanged(bool value) => Rebuild();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubstituteCommand))]
    private ObjectRowViewModel? _selectedRow;

    /// <summary>Reads the objects again. Rows still there are updated in place; new objects get a row, objects gone lose theirs.</summary>
    [RelayCommand]
    public void Refresh()
    {
        _syncingGroup = true;
        GroupByModel = Source.GroupObjects;
        _syncingGroup = false;

        _inSourceOrder = [];
        HashSet<string> seen = [];
        foreach (ObjectInfo info in Source.GetObjects())
        {
            if (!seen.Add(info.Id))
                continue; // an id names one object; a repeat is a fault in the source and would break the list
            if (_rowsById.TryGetValue(info.Id, out ObjectRowViewModel? row))
                row.Update(info);
            else
                _rowsById[info.Id] = row = new ObjectRowViewModel(info, this);
            _inSourceOrder.Add(row);
        }

        foreach (string gone in _rowsById.Keys.Except(_inSourceOrder.Select(r => r.Id)).ToList())
            _rowsById.Remove(gone);

        Rebuild();
        foreach (ObjectRowViewModel row in _inSourceOrder)
            row.RefreshLinks();
    }

    private bool CanSubstitute => SelectedRow is { Info.CanSubstitute: true };

    /// <summary>Substitutes the model of the selected row. The prototype hard-codes one model here; a table row is the natural choice.</summary>
    [RelayCommand(CanExecute = nameof(CanSubstitute))]
    private void Substitute()
    {
        if (SelectedRow is not null)
            _ = SubstituteAsync(SelectedRow);
    }

    internal async Task SubstituteAsync(ObjectRowViewModel row)
    {
        if (Source.GetObjectModel(row.Id) is not { } target)
            return;
        _shell.ShowOverlay(new SubstituteViewModel(target, await _catalog.GetCurrentAsync(target), _catalog));
    }

    /// <summary>Clicking a row opens it; clicking the open row closes it. At most one is open.</summary>
    internal void Select(ObjectRowViewModel row)
    {
        ObjectRowViewModel? next = ReferenceEquals(SelectedRow, row) ? null : row;
        SelectedRow = next;
        foreach (ObjectRowViewModel other in _rowsById.Values)
            other.IsSelected = ReferenceEquals(other, next);
    }

    internal void OnIgnoreChanged() => Rebuild();

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileViewModel.BroadcastTacpack) or nameof(ProfileViewModel.BroadcastEverything))
        {
            foreach (ObjectRowViewModel row in _rowsById.Values)
                row.RefreshLinks();
        }
    }

    private void Rebuild()
    {
        List<ObjectRowViewModel> visible = [.. _sort.Apply(_inSourceOrder).Where(r => ListIgnoredObjects || !r.IsIgnored)];
        CollectionSync.Reconcile(Rows, visible);

        if (SelectedRow is not null && !Rows.Contains(SelectedRow))
        {
            SelectedRow.IsSelected = false;
            SelectedRow = null;
        }
    }
}
