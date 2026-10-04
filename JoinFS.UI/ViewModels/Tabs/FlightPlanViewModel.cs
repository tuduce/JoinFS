using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

/// <summary>
/// Flight Plan tab: the plan filed for the user's aircraft, and "Import from SimBrief".
/// The SimBrief username is asked for once, ever: a stored one is used straight away.
/// </summary>
public sealed partial class FlightPlanViewModel : ObservableObject
{
    private readonly IFlightPlanStore _store;
    private readonly ISimBriefClient _simBrief;
    private readonly ProfileViewModel _profile;
    private readonly IShell _shell;

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

    /// <summary>Raised after a successful import, so the strip's flight-plan button can show "Loaded".</summary>
    public event EventHandler? Imported;

    public FlightPlanData ToData() => new(Callsign, Type, Rules, From, To, Altitude, Route, Remarks);

    /// <summary>Clearing leaves the callsign and type blank too, and the rules back on VFR.</summary>
    [RelayCommand]
    private void Clear() => Apply(new FlightPlanData("", "", "VFR", "", "", "", "", ""));

    [RelayCommand]
    private void Save() => _store.Save(ToData());

    [RelayCommand]
    private Task ImportFromSimbrief() => TryImportFromSimbriefAsync();

    /// <summary>
    /// Imports now if a SimBrief username is stored; otherwise asks for it once, then imports.
    /// Returns false when the import is waiting on that prompt rather than done.
    /// </summary>
    public async Task<bool> TryImportFromSimbriefAsync()
    {
        if (!_profile.HasSimbriefUsername)
        {
            _shell.ShowOverlay(new SimbriefPromptViewModel(username =>
            {
                _profile.SimbriefUsername = username;
                return PerformImportAsync();
            }));
            return false;
        }

        await PerformImportAsync();
        return true;
    }

    private async Task PerformImportAsync()
    {
        FlightPlanData fetched = await _simBrief.FetchAsync(_profile.SimbriefUsername, CancellationToken.None);

        // The aircraft's own callsign stays if it has one; everything else comes from SimBrief.
        Apply(fetched with { Callsign = string.IsNullOrEmpty(Callsign) ? fetched.Callsign : Callsign });
        Imported?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(FlightPlanData plan)
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
}
