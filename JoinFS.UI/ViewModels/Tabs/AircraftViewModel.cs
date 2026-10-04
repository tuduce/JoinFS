using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

public sealed partial class AircraftRowViewModel : ObservableObject
{
    private readonly AircraftViewModel _owner;

    private readonly RecordFlag _recordFlag;
    private readonly ActionLink _recordAction;

    internal AircraftRowViewModel(AircraftInfo info, AircraftViewModel owner, RecordFlag recordFlag)
    {
        Info = info;
        _owner = owner;
        _recordFlag = recordFlag;
        _recordFlag.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(Recording));
            _recordAction.Label = RecordActionLabel;
        };
        _ignoreAction = new ActionLink("Ignore", ToggleIgnoreCommand);
        _recordAction = new ActionLink(RecordActionLabel, new RelayCommand(() => Recording = !Recording));
        Actions = BuildActions();
    }

    public AircraftInfo Info { get; }
    public string Callsign => Info.Callsign;
    public string Owner => Info.Owner;
    public string Model => Info.Model;
    public string Distance => $"{Info.DistanceNm.ToString("N1", CultureInfo.CurrentCulture)} nm";
    public int Heading => Info.Heading;
    public string Altitude => $"{Info.AltitudeFt.ToString("N0", CultureInfo.CurrentCulture)} ft";
    public int GroundSpeed => Info.GroundSpeed;

    /// <summary>Far-away aircraft are flagged in orange.</summary>
    public bool IsFar => Info.DistanceNm > 3000;

    public string Squawk => Info.Squawk;
    public string Bearing => $"{Info.Heading}°";
    public string Com1 => Info.Com1;
    public string Com2 => Info.Com2;
    public string Simulator => Info.Simulator;
    public string OriginalModel => Info.OriginalModel;
    public string FlightPlan => Info.FlightPlan;
    public string Remarks => Info.Remarks;

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IgnoreLabel))]
    private bool _isIgnored;

    /// <summary>Included in the recording. The same flag as in the Recorder tab's list.</summary>
    public bool Recording
    {
        get => _recordFlag.IsOn;
        set => _recordFlag.IsOn = value;
    }

    private string RecordActionLabel => Recording ? "Remove From Recorder" : "Add To Recorder";

    public string IgnoreLabel => IsIgnored ? "Unignore" : "Ignore";

    /// <summary>Every link of the Actions block, in the order the design lays them out, three to a row.</summary>
    public IReadOnlyList<ActionLink> Actions { get; }

    private readonly ActionLink _ignoreAction;

    partial void OnIsIgnoredChanged(bool value) => _ignoreAction.Label = IgnoreLabel;

    [RelayCommand]
    private void ToggleExpanded() => _owner.Expand(this);

    [RelayCommand]
    private void ToggleIgnore() => IsIgnored = !IsIgnored;

    [RelayCommand]
    private void Substitute() => _owner.Substitute(this);

    [RelayCommand]
    private void ExplainMatch() => _owner.ExplainMatch(this);

    [RelayCommand]
    private void AssignVariables() => _owner.AssignVariables(this);

    [RelayCommand]
    private void AdjustHeight() => _owner.AdjustHeight(this);

    [RelayCommand]
    private void CopyFlightPlan() => _owner.CopyFlightPlan(this);

    // The links the prototype shows but gives no behaviour. They need the sim thread (follow, enter cockpit, track, weather)
    // or the recorder; each is wired when its tab is.
    private static readonly IRelayCommand NotWired = new RelayCommand(() => { });

    // "Stop Tracking" is drawn but never available in the design.
    private static readonly IRelayCommand Unavailable = new RelayCommand(() => { }, () => false);

    private IReadOnlyList<ActionLink> BuildActions() =>
    [
        new("Substitute…", SubstituteCommand),
        new("Explain Match…", ExplainMatchCommand),
        new("Copy Flight Plan…", CopyFlightPlanCommand),
        new("Assign Variables…", AssignVariablesCommand),
        new("Adjust Height…", AdjustHeightCommand),
        new($"Follow '{Callsign}'", NotWired),
        new("Enter Cockpit", NotWired),
        new("Track Heading On Hdg", NotWired),
        new("Track Bearing On Hdg", NotWired),
        new("Copy Weather", NotWired),
        _recordAction,
        // The two "Include All …" links act on the whole list rather than on this aircraft.
        new("Include All Hub Aircraft", new RelayCommand(_owner.IncludeHubAircraft)),
        new("Include All Simulator Aircraft", new RelayCommand(_owner.IncludeSimulatorAircraft)),
        _ignoreAction,
        new("Stop Tracking", Unavailable),
    ];
}

/// <summary>Aircraft tab: every aircraft the session lists, sortable, with details and actions in the expanded row.</summary>
public sealed partial class AircraftViewModel : ObservableObject
{
    private readonly ITrafficSource _traffic;
    private readonly IModelCatalog _catalog;
    private readonly IPlatform _platform;
    private readonly ProfileViewModel _profile;
    private readonly IShell _shell;
    private readonly SortController<AircraftRowViewModel> _sort;
    private readonly Dictionary<string, AircraftRowViewModel> _rowsByCallsign = [];
    private List<AircraftRowViewModel> _inSourceOrder = [];

    private readonly RecordSelection _recordSelection;

    public AircraftViewModel(ITrafficSource traffic, IModelCatalog catalog, IPlatform platform, ProfileViewModel profile, RecordSelection recordSelection, IShell shell)
    {
        _recordSelection = recordSelection;
        _traffic = traffic;
        _catalog = catalog;
        _platform = platform;
        _profile = profile;
        _shell = shell;

        _sort = new SortController<AircraftRowViewModel>(Rebuild);
        CallsignColumn = _sort.Add("callsign", "Callsign", r => r.Callsign);
        OwnerColumn = _sort.Add("owner", "Owner", r => r.Owner);
        DistanceColumn = _sort.Add("distance", "Distance", r => r.Info.DistanceNm);
        HeadingColumn = _sort.Add("heading", "Heading", r => r.Heading);
        AltitudeColumn = _sort.Add("altitude", "Altitude", r => r.Info.AltitudeFt);
        GroundSpeedColumn = _sort.Add("gs", "GS", r => r.GroundSpeed);
        ModelColumn = _sort.Add("model", "Sub Model", r => r.Model);

        Refresh();
    }

    public SortColumn CallsignColumn { get; }
    public SortColumn OwnerColumn { get; }
    public SortColumn DistanceColumn { get; }
    public SortColumn HeadingColumn { get; }
    public SortColumn AltitudeColumn { get; }
    public SortColumn GroundSpeedColumn { get; }
    public SortColumn ModelColumn { get; }

    public ObservableCollection<AircraftRowViewModel> Rows { get; } = [];

    public int AircraftCount => Rows.Count;

    [RelayCommand]
    public void Refresh()
    {
        // Rows outlive a refresh, so what the user did (expanded, ignored, record ticks) survives it.
        _inSourceOrder = [];
        foreach (AircraftInfo info in _traffic.GetAircraft())
        {
            if (!_rowsByCallsign.TryGetValue(info.Callsign, out AircraftRowViewModel? row))
                _rowsByCallsign[info.Callsign] = row = new AircraftRowViewModel(info, this, _recordSelection.For(info.Callsign));
            _inSourceOrder.Add(row);
        }
        Rebuild();
    }

    /// <summary>Records every aircraft flown by another pilot on the hub.</summary>
    internal void IncludeHubAircraft()
    {
        foreach (AircraftRowViewModel row in _inSourceOrder.Where(r => r.Owner.Length > 0))
            row.Recording = true;
    }

    /// <summary>Records every aircraft of the local simulator, which have no other pilot as owner.</summary>
    internal void IncludeSimulatorAircraft()
    {
        foreach (AircraftRowViewModel row in _inSourceOrder.Where(r => r.Owner.Length == 0))
            row.Recording = true;
    }

    internal void Expand(AircraftRowViewModel row)
    {
        bool open = !row.IsExpanded;
        foreach (AircraftRowViewModel other in Rows)
            other.IsExpanded = false;
        row.IsExpanded = open;
    }

    internal void Substitute(AircraftRowViewModel row) =>
        _shell.ShowOverlay(new SubstituteViewModel(row.OriginalModel, _profile.GetOverride(row.OriginalModel) ?? row.Model, _catalog, _profile));

    internal void ExplainMatch(AircraftRowViewModel row) =>
        _shell.ShowOverlay(new ExplainMatchViewModel(row.Model, _catalog, _platform));

    internal void AssignVariables(AircraftRowViewModel row) =>
        _shell.ShowOverlay(new VariablesOverlayViewModel(row.Model, ["ListBox_Sets"], _platform));

    internal void AdjustHeight(AircraftRowViewModel row) =>
        _shell.ShowOverlay(new AdjustHeightViewModel(row.Model, _profile));

    internal void CopyFlightPlan(AircraftRowViewModel row) => _ = _platform.CopyTextAsync(row.FlightPlan);

    private void Rebuild()
    {
        // With no column chosen the rows stay in the source's own order.
        List<AircraftRowViewModel> sorted = [.. _sort.Apply(_inSourceOrder)];
        Rows.Clear();
        foreach (AircraftRowViewModel row in sorted)
            Rows.Add(row);
        OnPropertyChanged(nameof(AircraftCount));
    }
}
