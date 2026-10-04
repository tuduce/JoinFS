using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

public sealed partial class ObjectRowViewModel : ObservableObject
{
    private readonly ObjectsViewModel _owner;
    private readonly RelayCommand _thisObjectCommand;
    private readonly ActionLink _thisObjectLink;
    private readonly ActionLink _modelLink;
    private readonly ActionLink _tacpackLink;
    private readonly ActionLink _everythingLink;

    internal ObjectRowViewModel(ObjectInfo info, ObjectsViewModel owner)
    {
        Info = info;
        _owner = owner;
        _broadcast = info.Broadcast;
        _ignoreOwner = info.IgnoreOwner;
        _ignoreModel = info.IgnoreModel;

        // The four checkboxes of the old BroadcastForm, as links: this object, every object of this model, the VRS TacPack, everything.
        _thisObjectCommand = new RelayCommand(() => Broadcast = !Broadcast, () => !_owner.GroupByModel);
        _thisObjectLink = new ActionLink("", _thisObjectCommand);
        _modelLink = new ActionLink("", new RelayCommand(() => _owner.ToggleModelBroadcast(Model)));
        _tacpackLink = new ActionLink("", new RelayCommand(() => _owner.Profile.BroadcastTacpack = !_owner.Profile.BroadcastTacpack));
        _everythingLink = new ActionLink("", new RelayCommand(() => _owner.Profile.BroadcastEverything = !_owner.Profile.BroadcastEverything));

        Actions =
        [
            new ActionLink("Substitute…", new RelayCommand(() => _owner.Substitute(this))),
            _thisObjectLink,
            _modelLink,
            _tacpackLink,
            _everythingLink,
        ];
        RefreshLinks();
    }

    public ObjectInfo Info { get; }
    public string Owner => Info.Owner;
    public string Model => Info.Model;
    public int Count => Info.Count;
    public int Bearing => Info.Bearing;
    public string Distance => $"{Info.DistanceNm.ToString("N1", CultureInfo.CurrentCulture)} nm";

    [ObservableProperty]
    private bool _broadcast;

    partial void OnBroadcastChanged(bool value) => RefreshLinks();

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

    partial void OnIgnoreOwnerChanged(bool value) => _owner.OnIgnoreChanged();
    partial void OnIgnoreModelChanged(bool value) => _owner.OnIgnoreChanged();

    /// <summary>Words each broadcast link by its current state, and greys "this object" while grouped by model.</summary>
    internal void RefreshLinks()
    {
        _thisObjectLink.Label = Broadcast ? "Stop Broadcasting This Object" : "Broadcast This Object";
        _thisObjectCommand.NotifyCanExecuteChanged();
        _modelLink.Label = _owner.IsModelBroadcast(Model) ? $"Stop Broadcasting All '{Model}'" : $"Broadcast All '{Model}'";
        _tacpackLink.Label = _owner.Profile.BroadcastTacpack ? "Stop Broadcasting VRS TacPack" : "Broadcast VRS TacPack";
        _everythingLink.Label = _owner.Profile.BroadcastEverything ? "Stop Broadcasting Everything" : "Broadcast Everything";
    }
}

/// <summary>Objects tab: scenery and shared objects, laid out like the Aircraft table, with broadcasting in the expanded row.</summary>
public sealed partial class ObjectsViewModel : ObservableObject
{
    private readonly ITrafficSource _traffic;
    private readonly IModelCatalog _catalog;
    private readonly IShell _shell;
    private readonly SortController<ObjectRowViewModel> _sort;
    private readonly Dictionary<string, ObjectRowViewModel> _rowsByKey = [];
    private readonly HashSet<string> _broadcastModels = [];
    private List<ObjectRowViewModel> _inSourceOrder = [];

    public ObjectsViewModel(ITrafficSource traffic, IModelCatalog catalog, ProfileViewModel profile, IShell shell)
    {
        _traffic = traffic;
        _catalog = catalog;
        Profile = profile;
        _shell = shell;

        _sort = new SortController<ObjectRowViewModel>(Rebuild);
        OwnerColumn = _sort.Add("owner", "Owner", r => r.Owner);
        ModelColumn = _sort.Add("model", "Model", r => r.Model);
        CountColumn = _sort.Add("count", "Count", r => r.Count);
        BearingColumn = _sort.Add("bearing", "Bearing", r => r.Bearing);
        DistanceColumn = _sort.Add("distance", "Distance", r => r.Info.DistanceNm);

        // The TacPack and Everything links read the profile, which Settings → Simulator also edits.
        profile.PropertyChanged += OnProfileChanged;

        Refresh();
    }

    public ProfileViewModel Profile { get; }

    public SortColumn OwnerColumn { get; }
    public SortColumn ModelColumn { get; }
    public SortColumn CountColumn { get; }
    public SortColumn BearingColumn { get; }
    public SortColumn DistanceColumn { get; }

    public ObservableCollection<ObjectRowViewModel> Rows { get; } = [];

    /// <summary>Display-only for now; the README leaves the grouping rule to the wiring step.
    /// While on, "Broadcast This Object" is unavailable, as in the old Broadcast dialog.</summary>
    [ObservableProperty]
    private bool _groupByModel;

    partial void OnGroupByModelChanged(bool value) => RefreshLinks();

    [ObservableProperty]
    private bool _listIgnoredObjects;

    partial void OnListIgnoredObjectsChanged(bool value) => Rebuild();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SubstituteCommand))]
    private ObjectRowViewModel? _selectedRow;

    [RelayCommand]
    public void Refresh()
    {
        // The prototype keys an owner's tick boxes by owner, so one row per owner and model is kept across refreshes.
        _inSourceOrder = [];
        foreach (ObjectInfo info in _traffic.GetObjects())
        {
            string key = $"{info.Owner}\u0000{info.Model}";
            if (!_rowsByKey.TryGetValue(key, out ObjectRowViewModel? row))
                _rowsByKey[key] = row = new ObjectRowViewModel(info, this);
            _inSourceOrder.Add(row);
        }
        Rebuild();
    }

    private bool CanSubstitute => SelectedRow is not null;

    /// <summary>Substitutes the model of the selected row. The prototype hard-codes one model here; a table row is the natural choice.</summary>
    [RelayCommand(CanExecute = nameof(CanSubstitute))]
    private void Substitute()
    {
        if (SelectedRow is not null)
            Substitute(SelectedRow);
    }

    internal void Substitute(ObjectRowViewModel row)
    {
        string original = ModelNames.StripVariantSuffix(row.Model);
        _shell.ShowOverlay(new SubstituteViewModel(original, Profile.GetOverride(original) ?? row.Model, _catalog, Profile));
    }

    /// <summary>Clicking a row opens it; clicking the open row closes it. At most one is open.</summary>
    internal void Select(ObjectRowViewModel row)
    {
        ObjectRowViewModel? next = ReferenceEquals(SelectedRow, row) ? null : row;
        SelectedRow = next;
        foreach (ObjectRowViewModel other in _rowsByKey.Values)
            other.IsSelected = ReferenceEquals(other, next);
    }

    internal bool IsModelBroadcast(string model) => _broadcastModels.Contains(model);

    internal void ToggleModelBroadcast(string model)
    {
        if (!_broadcastModels.Remove(model))
            _broadcastModels.Add(model);
        RefreshLinks();
    }

    internal void OnIgnoreChanged() => Rebuild();

    private void OnProfileChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProfileViewModel.BroadcastTacpack) or nameof(ProfileViewModel.BroadcastEverything))
            RefreshLinks();
    }

    private void RefreshLinks()
    {
        foreach (ObjectRowViewModel row in _rowsByKey.Values)
            row.RefreshLinks();
    }

    private void Rebuild()
    {
        IEnumerable<ObjectRowViewModel> visible = _sort.Apply(_inSourceOrder).Where(r => ListIgnoredObjects || !r.IsIgnored);
        Rows.Clear();
        foreach (ObjectRowViewModel row in visible)
            Rows.Add(row);
        if (SelectedRow is not null && !Rows.Contains(SelectedRow))
            Select(SelectedRow);
    }
}
