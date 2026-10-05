using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// Flight Plan tab: the plan filed for the user's aircraft, and "Import from SimBrief".
/// The SimBrief username is asked for once, ever: a stored one is used straight away.
/// The tab shows the live plan until the user edits it; from then on their edits stay, unsaved, until Save or Clear.
/// </summary>
public sealed partial class FlightPlanViewModel : ObservableObject
{
    private readonly IFlightPlanStore _store;
    private readonly ISimBriefClient _simBrief;
    private readonly ProfileViewModel _profile;
    private readonly IShell _shell;

    // Set while the fields are being filled from somewhere else, so that is not taken for an edit.
    private bool _applying;
    private bool _hasUnsavedEdits;

    public FlightPlanViewModel(IFlightPlanStore store, ISimBriefClient simBrief, ProfileViewModel profile, IShell shell)
    {
        _store = store;
        _simBrief = simBrief;
        _profile = profile;
        _shell = shell;
        Apply(store.Load());
    }

    public IReadOnlyList<string> RulesOptions { get; } = ["VFR", "IFR"];

    [ObservableProperty] private string _callsign = "";
    [ObservableProperty] private string _type = "";
    [ObservableProperty] private string _rules = "VFR";
    [ObservableProperty] private string _from = "";
    [ObservableProperty] private string _to = "";
    [ObservableProperty] private string _altitude = "";
    [ObservableProperty] private string _route = "";
    [ObservableProperty] private string _remarks = "";

    /// <summary>What the last import did, in words: the route imported, or that SimBrief had nothing. Empty otherwise.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>Raised after a successful import, so the strip's flight-plan button can show "Loaded".</summary>
    public event EventHandler? Imported;

    /// <summary>Raised after Save, with whether the plan saved is a filled one (true) or a cleared one (false).</summary>
    public event EventHandler<bool>? Saved;

    public FlightPlanData ToData() => new(Callsign, Type, Rules, From, To, Altitude, Route, Remarks);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (!_applying && e.PropertyName is nameof(Callsign) or nameof(Type) or nameof(Rules) or nameof(From) or nameof(To) or nameof(Altitude) or nameof(Route) or nameof(Remarks))
            _hasUnsavedEdits = true;
    }

    /// <summary>Reads the live plan again, unless the user is in the middle of editing it.</summary>
    public void Refresh()
    {
        if (!_hasUnsavedEdits)
            Apply(_store.Load());
    }

    /// <summary>Clearing leaves the callsign and type blank too, and the rules back on VFR. It takes Save to make it the plan.</summary>
    [RelayCommand]
    private void Clear()
    {
        Apply(new FlightPlanData("", "", "VFR", "", "", "", "", ""));
        _hasUnsavedEdits = true;
        Status = "";
        _simBrief.Reset();
    }

    /// <summary>Makes the fields the user's plan. The plan is then read back: the live app fills what was left blank.</summary>
    [RelayCommand]
    private void Save()
    {
        FlightPlanData plan = ToData();
        _store.Save(plan);
        _hasUnsavedEdits = false;
        Apply(_store.Load());
        Saved?.Invoke(this, !plan.IsBlank);
    }

    /// <summary>The link on the tab: the imported plan is shown to be checked and saved.</summary>
    [RelayCommand]
    private Task ImportFromSimbrief() => TryImportFromSimbriefAsync(commit: false);

    /// <summary>
    /// Imports now if a SimBrief username is stored; otherwise asks for it once, then imports.
    /// Returns false when the import is waiting on that prompt rather than done.
    /// With <paramref name="commit"/> the plan is made the user's at once and sent to the network, as the strip's button does.
    /// </summary>
    public async Task<bool> TryImportFromSimbriefAsync(bool commit = false)
    {
        if (!_profile.HasSimbriefUsername)
        {
            _shell.ShowOverlay(new SimbriefPromptViewModel(username =>
            {
                _profile.SimbriefUsername = username;
                return PerformImportAsync(commit);
            }));
            return false;
        }

        await PerformImportAsync(commit);
        return true;
    }

    private async Task PerformImportAsync(bool commit)
    {
        Status = "";
        FlightPlanData? fetched = await _simBrief.FetchAsync(_profile.SimbriefUsername, commit, CancellationToken.None);
        if (fetched is null)
        {
            // What is shown stays: a failed import never blanks the plan.
            Status = "SimBrief has no flight plan for this user.";
            return;
        }

        // SimBrief knows the callsign it was filed under; if it gave none, the aircraft's own stays.
        Apply(fetched with { Callsign = string.IsNullOrEmpty(fetched.Callsign) ? Callsign : fetched.Callsign });
        _hasUnsavedEdits = !commit;
        Status = $"Imported {fetched.From} → {fetched.To}";
        Imported?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(FlightPlanData plan)
    {
        _applying = true;
        try
        {
            Callsign = plan.Callsign;
            Type = plan.Type;
            Rules = plan.Rules;
            From = plan.From;
            To = plan.To;
            Altitude = plan.Altitude;
            Route = plan.Route;
            Remarks = plan.Remarks;
        }
        finally
        {
            _applying = false;
        }
    }
}
