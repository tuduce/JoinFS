using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// One aircraft in the Aircraft table. The row lives as long as the aircraft is listed and is updated in place on each refresh, so an open
/// row survives it. Recording and ignoring are written straight to the service; every other action is a link that the service says is available.
/// </summary>
public sealed partial class AircraftRowViewModel : ObservableObject
{
    private readonly AircraftViewModel _owner;
    private readonly RecordFlag _recordFlag;

    // True while the row is being filled from the service, so what it reads is not written back as if the user had set it.
    private bool _syncing;

    private readonly ActionLink _substitute, _explain, _copyFlightPlan, _variables, _height, _follow, _enter, _trackHeading, _trackBearing,
        _copyWeather, _record, _includeHub, _includeSimulator, _ignore, _stopTracking;

    internal AircraftRowViewModel(AircraftInfo info, AircraftViewModel owner, RecordFlag recordFlag)
    {
        Info = info;
        _owner = owner;
        _recordFlag = recordFlag;
        _recordFlag.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Recording));
            RefreshActions();
            if (!_syncing)
                _owner.Source.SetRecording(Id, _recordFlag.IsOn);
        };

        _substitute = new(Loc.T("Substitute…"), new RelayCommand(() => _ = _owner.SubstituteAsync(this), () => Info.Can.HasFlag(AircraftActions.Substitute)));
        _explain = new(Loc.T("Explain Match…"), new RelayCommand(() => _ = _owner.ExplainMatchAsync(this), () => Info.Can.HasFlag(AircraftActions.ExplainMatch)));
        _copyFlightPlan = new(Loc.T("Copy Flight Plan…"), new RelayCommand(() => _owner.CopyFlightPlan(this), () => Info.Can.HasFlag(AircraftActions.FlightPlan)));
        _variables = new(Loc.T("Assign Variables…"), new RelayCommand(() => _owner.AssignVariables(this), () => Info.Can.HasFlag(AircraftActions.Variables)));
        _height = new(Loc.T("Adjust Height…"), new RelayCommand(() => _owner.AdjustHeight(this), () => Info.Can.HasFlag(AircraftActions.AdjustHeight)));
        _follow = new("", new RelayCommand(() => _owner.Source.Follow(Id), () => Info.Can.HasFlag(AircraftActions.Follow)));
        _enter = new("", new RelayCommand(() => _owner.Source.EnterCockpit(Id), () => _owner.InCockpit || Info.Can.HasFlag(AircraftActions.EnterCockpit)));
        _trackHeading = new(Loc.T("Track Heading On Hdg"), new RelayCommand(() => _owner.Source.TrackHeading(Id), () => Info.Can.HasFlag(AircraftActions.Track)));
        _trackBearing = new(Loc.T("Track Bearing On Hdg"), new RelayCommand(() => _owner.Source.TrackBearing(Id), () => Info.Can.HasFlag(AircraftActions.Track)));
        _copyWeather = new(Loc.T("Copy Weather"), new RelayCommand(() => _owner.Source.CopyWeather(Id), () => Info.Can.HasFlag(AircraftActions.CopyWeather)));
        _record = new("", new RelayCommand(() => Recording = !Recording, () => Info.Can.HasFlag(AircraftActions.Record)));
        _includeHub = new("", new RelayCommand(_owner.ToggleIncludeHubAircraft));
        _includeSimulator = new("", new RelayCommand(_owner.ToggleIncludeSimulatorAircraft));
        _ignore = new("", new RelayCommand(() => _owner.SetIgnored(this, !IsIgnored), () => Info.Can.HasFlag(AircraftActions.Ignore)));
        _stopTracking = new(Loc.T("Stop Tracking"), new RelayCommand(_owner.Source.StopTracking, () => _owner.IsTracking));

        // The order the design lays them out in, three to a row.
        Actions =
        [
            _substitute, _explain, _copyFlightPlan, _variables, _height, _follow, _enter, _trackHeading, _trackBearing, _copyWeather,
            _record, _includeHub, _includeSimulator, _ignore, _stopTracking,
        ];
        RefreshActions();
    }

    public AircraftInfo Info { get; private set; }
    public string Id => Info.Id;
    public string Callsign => Info.Callsign;
    public string Owner => Info.Owner;
    public string Model => Info.Model;

    // What the columns sort by: numbers as numbers, and an unknown value before every known one.
    public int HeadingValue => Info.Heading ?? -1;
    public double DistanceValue => Info.DistanceNm ?? -1;
    public int AltitudeValue => Info.AltitudeFt ?? int.MinValue;
    public double SpeedValue => Info.SpeedKnots;

    public string Distance => Info.DistanceNm is { } d ? $"{d.ToString("N1", CultureInfo.CurrentCulture)} nm" : "-";
    public string Heading => Info.Heading is { } h ? h.ToString("D3", CultureInfo.InvariantCulture) : "-";
    public string Altitude => Info.AltitudeFt is { } a ? $"{a.ToString("N0", CultureInfo.CurrentCulture)} ft" : "-";

    /// <summary>Knots, or Mach from 600 knots up, as the old window showed it.</summary>
    public string GroundSpeed => Info.SpeedKnots > 600
        ? $"{(Info.SpeedKnots / 667.0).ToString("N2", CultureInfo.CurrentCulture)} M"
        : $"{Info.SpeedKnots.ToString("N0", CultureInfo.CurrentCulture)} kt";

    /// <summary>Far-away aircraft are flagged in orange.</summary>
    public bool IsFar => Info.DistanceNm > 3000 && !IsFailed;

    /// <summary>The simulator refused the aircraft: its distance is red.</summary>
    public bool IsFailed => Info.Link == AircraftLinkState.Failed;

    public string Squawk => Info.Squawk;
    public string Bearing => Info.Bearing is { } b ? $"{b.ToString("D3", CultureInfo.InvariantCulture)}°" : "-";
    public string Com1 => Info.Com1;
    public string Com2 => Info.Com2;
    public string Simulator => Info.Simulator;
    public string OriginalModel => Info.OriginalModel;
    public string FlightPlan => Info.FlightPlan;
    public string Remarks => Info.Remarks;

    /// <summary>The aircraft the view follows by heading or bearing: highlighted.</summary>
    public bool IsTracked => Info.Tracked;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreLabel))]
    private bool _isIgnored;

    public string IgnoreLabel => IsIgnored ? Loc.T("Unignore") : Loc.T("Ignore");

    /// <summary>Included in the recording. The same flag as in the Recorder tab's list.</summary>
    public bool Recording
    {
        get => _recordFlag.IsOn;
        set
        {
            if (Info.Can.HasFlag(AircraftActions.Record) || _syncing)
                _recordFlag.IsOn = value;
        }
    }

    /// <summary>False for an aircraft the recorder plays or one of another hub: its box is disabled.</summary>
    public bool CanRecord => Info.Can.HasFlag(AircraftActions.Record);

    /// <summary>Every link of the Actions block, in the order the design lays them out, three to a row.</summary>
    public IReadOnlyList<ActionLink> Actions { get; }

    /// <summary>Takes a newer reading of the same aircraft.</summary>
    internal void Update(AircraftInfo info)
    {
        Info = info;
        _syncing = true;
        try
        {
            _recordFlag.IsOn = info.Recording;
            IsIgnored = info.Ignored;
        }
        finally
        {
            _syncing = false;
        }
        OnPropertyChanged(string.Empty); // every display property may have changed
        RefreshActions();
    }

    /// <summary>Words each link by the state it acts on, and re-asks whether it is available.</summary>
    internal void RefreshActions()
    {
        _follow.Label = Loc.F("Follow '{0}'", Callsign);
        _enter.Label = _owner.InCockpit ? Loc.T("Leave Cockpit") : Loc.T("Enter Cockpit");
        _record.Label = Recording ? Loc.T("Remove From Recorder") : Loc.T("Add To Recorder");
        _includeHub.Label = _owner.IncludeHubAircraft ? Loc.T("Exclude Hub Aircraft") : Loc.T("Include All Hub Aircraft");
        _includeSimulator.Label = _owner.IncludeSimulatorAircraft ? Loc.T("Exclude Simulator Aircraft") : Loc.T("Include All Simulator Aircraft");
        _ignore.Label = IgnoreLabel;

        foreach (ActionLink link in Actions)
            (link.Command as RelayCommand)?.NotifyCanExecuteChanged();
    }

    partial void OnIsIgnoredChanged(bool value) => RefreshActions();

    [RelayCommand]
    private void ToggleExpanded() => _owner.Expand(this);

    /// <summary>Shows an ignore state the service already holds, without writing it again.</summary>
    internal void ShowIgnored(bool ignored)
    {
        _syncing = true;
        try
        {
            IsIgnored = ignored;
        }
        finally
        {
            _syncing = false;
        }
    }
}

/// <summary>Aircraft tab: every aircraft the session lists, sortable, with details and actions in the expanded row.</summary>
public sealed partial class AircraftViewModel : ObservableObject
{
    private readonly IModelCatalog _catalog;
    private readonly IVariablesCatalog _variables;
    private readonly IPlatform _platform;
    private readonly ProfileViewModel _profile;
    private readonly IShell _shell;
    private readonly RecordSelection _recordSelection;
    private readonly SortController<AircraftRowViewModel> _sort;
    private readonly Dictionary<string, AircraftRowViewModel> _rowsById = [];
    private List<AircraftRowViewModel> _inSourceOrder = [];

    public AircraftViewModel(ITrafficSource traffic, IModelCatalog catalog, IVariablesCatalog variables, IPlatform platform, ProfileViewModel profile, RecordSelection recordSelection, IShell shell)
    {
        Source = traffic;
        _catalog = catalog;
        _variables = variables;
        _platform = platform;
        _profile = profile;
        _recordSelection = recordSelection;
        _shell = shell;

        _sort = new SortController<AircraftRowViewModel>(Rebuild);
        CallsignColumn = _sort.Add("callsign", Loc.T("Callsign"), r => r.Callsign);
        OwnerColumn = _sort.Add("owner", Loc.T("Owner"), r => r.Owner);
        DistanceColumn = _sort.Add("distance", Loc.T("Distance"), r => r.DistanceValue);
        HeadingColumn = _sort.Add("heading", Loc.T("Heading"), r => r.HeadingValue);
        AltitudeColumn = _sort.Add("altitude", Loc.T("Altitude"), r => r.AltitudeValue);
        GroundSpeedColumn = _sort.Add("gs", Loc.T("GS"), r => r.SpeedValue);
        ModelColumn = _sort.Add("model", Loc.T("Sub Model"), r => r.Model);

        Refresh();
    }

    internal ITrafficSource Source { get; }

    public SortColumn CallsignColumn { get; }
    public SortColumn OwnerColumn { get; }
    public SortColumn DistanceColumn { get; }
    public SortColumn HeadingColumn { get; }
    public SortColumn AltitudeColumn { get; }
    public SortColumn GroundSpeedColumn { get; }
    public SortColumn ModelColumn { get; }

    public ObservableCollection<AircraftRowViewModel> Rows { get; } = [];

    public int AircraftCount => Rows.Count;

    // What the actions read; set at each refresh.
    internal bool InCockpit { get; private set; }
    internal bool IsTracking { get; private set; }
    internal bool IncludeHubAircraft => Source.IncludeHubAircraft;
    internal bool IncludeSimulatorAircraft => Source.IncludeSimulatorAircraft;

    /// <summary>Reads the aircraft again. Rows of aircraft still there are updated in place; new aircraft get a row, aircraft gone lose theirs.</summary>
    [RelayCommand]
    public void Refresh()
    {
        InCockpit = Source.InCockpit;
        IsTracking = Source.IsTracking;

        _inSourceOrder = [];
        HashSet<string> seen = [];
        foreach (AircraftInfo info in Source.GetAircraft())
        {
            if (!seen.Add(info.Id))
                continue; // an id names one aircraft; a repeat is a fault in the source and would break the list
            if (_rowsById.TryGetValue(info.Id, out AircraftRowViewModel? row))
                row.Update(info);
            else
                _rowsById[info.Id] = row = NewRow(info);
            _inSourceOrder.Add(row);
        }

        foreach (string gone in _rowsById.Keys.Except(_inSourceOrder.Select(r => r.Id)).ToList())
            _rowsById.Remove(gone);

        Rebuild();

        // The links that read the whole list's state (cockpit, tracking, the two filters) are worded on each row.
        foreach (AircraftRowViewModel row in _inSourceOrder)
            row.RefreshActions();
    }

    private AircraftRowViewModel NewRow(AircraftInfo info)
    {
        AircraftRowViewModel row = new(info, this, _recordSelection.For(info.Id));
        row.Update(info); // fills the flag and the ignore state without writing them back
        return row;
    }

    internal void Expand(AircraftRowViewModel row)
    {
        bool open = !row.IsExpanded;
        foreach (AircraftRowViewModel other in Rows)
            other.IsExpanded = false;
        row.IsExpanded = open;
    }

    internal void SetIgnored(AircraftRowViewModel row, bool ignored)
    {
        Source.SetIgnored(row.Id, ignored);
        row.ShowIgnored(ignored);
    }

    internal void ToggleIncludeHubAircraft()
    {
        Source.IncludeHubAircraft = !Source.IncludeHubAircraft;
        Refresh();
    }

    internal void ToggleIncludeSimulatorAircraft()
    {
        Source.IncludeSimulatorAircraft = !Source.IncludeSimulatorAircraft;
        Refresh();
    }

    internal async Task SubstituteAsync(AircraftRowViewModel row)
    {
        // The model as its owner has it; whether it is a match or your own masquerade is the service's to say.
        if (Source.GetAircraftModel(row.Id) is not { } target)
            return;
        _shell.ShowOverlay(new SubstituteViewModel(target, await _catalog.GetCurrentAsync(target), _catalog));
    }

    internal async Task ExplainMatchAsync(AircraftRowViewModel row)
    {
        if (await Source.ExplainMatchAsync(row.Id) is { } explanation)
            _shell.ShowOverlay(new ExplainMatchViewModel(explanation, _catalog, _platform));
    }

    internal void AssignVariables(AircraftRowViewModel row)
    {
        if (Source.GetVariablesModel(row.Id) is { } model)
            _shell.ShowOverlay(new VariablesOverlayViewModel(model, _variables, _catalog, _platform));
    }

    internal void AdjustHeight(AircraftRowViewModel row)
    {
        if (Source.GetHeightAdjustment(row.Id) is { } current)
            _shell.ShowOverlay(new AdjustHeightViewModel(current, cm => Source.SetHeightAdjustment(row.Id, cm)));
    }

    internal void CopyFlightPlan(AircraftRowViewModel row) => _ = _platform.CopyTextAsync(row.FlightPlan);

    private void Rebuild()
    {
        // With no column chosen the rows stay in the source's own order.
        CollectionSync.Reconcile(Rows, [.. _sort.Apply(_inSourceOrder)]);
        OnPropertyChanged(nameof(AircraftCount));
    }
}
