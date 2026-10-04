using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Tests;

/// <summary>A SimBrief whose state and answer a test decides, and that remembers how it was asked.</summary>
internal sealed class ScriptedSimBrief : ISimBriefClient
{
    public bool ReportsState => true;
    public ConnectionState State { get; set; } = ConnectionState.Disconnected;
    public void Poll() { }

    public FlightPlanData? Answer { get; set; } = new("DLH123", "A320", "IFR", "EDDF", "EGLL", "FL350", "ANEKI UL607 DVR", "RMK/TEST");
    public List<bool> Commits { get; } = [];
    public int Resets { get; private set; }

    public void Reset() => Resets++;

    public Task<FlightPlanData?> FetchAsync(string username, bool commit, CancellationToken cancellationToken)
    {
        Commits.Add(commit);
        return Task.FromResult(Answer);
    }
}

/// <summary>A flight plan store whose plan a test can change behind the tab's back, as the simulator does.</summary>
internal sealed class MovingFlightPlanStore : IFlightPlanStore
{
    public FlightPlanData Plan { get; set; } = new("", "", "VFR", "", "", "", "", "");
    public List<FlightPlanData> Saved { get; } = [];

    /// <summary>What a blank callsign becomes when saved: the aircraft's own, as in the live app.</summary>
    public string OwnCallsign { get; set; } = "";

    public FlightPlanData Load() => Plan;

    public void Save(FlightPlanData plan)
    {
        Saved.Add(plan);
        Plan = plan.Callsign.Length == 0 ? plan with { Callsign = OwnCallsign } : plan;
    }
}

public class FlightPlanLiveTests
{
    private static (MainViewModel Main, ScriptedSimBrief SimBrief, MovingFlightPlanStore Store) Open(string? simbriefUser = "pilot")
    {
        ScriptedSimBrief simBrief = new();
        MovingFlightPlanStore store = new();
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me", SimbriefUsername = simbriefUser }) with
        {
            SimBrief = simBrief,
            FlightPlan = store,
        };
        return (new MainViewModel(services), simBrief, store);
    }

    // ---- the strip's button and the tab's link

    [Fact]
    public async Task The_strips_button_commits_what_it_fetches_and_the_tabs_link_only_shows_it()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, _) = Open();

        await main.FlightPlanLoad.ToggleCommand.ExecuteAsync(null);
        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null);

        Assert.Equal([true, false], simBrief.Commits);
    }

    [Fact]
    public async Task The_strips_button_shows_the_state_the_live_app_reports()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, _) = Open();

        simBrief.State = ConnectionState.Connecting;
        main.Poll();
        Assert.True(main.FlightPlanLoad.IsConnecting);

        simBrief.State = ConnectionState.Connected;
        main.Poll();
        Assert.Equal("Loaded", main.FlightPlanLoad.StateLabel);

        simBrief.State = ConnectionState.Disconnected; // asked and found nothing
        main.Poll();
        Assert.Equal("Not loaded", main.FlightPlanLoad.StateLabel);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Clicking_Loaded_forgets_the_fetch_and_keeps_the_plan()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, MovingFlightPlanStore store) = Open();
        store.Plan = new FlightPlanData("DLH123", "A320", "IFR", "EDDF", "EGLL", "", "", "");
        simBrief.State = ConnectionState.Connected;
        main.Poll();

        await main.FlightPlanLoad.ToggleCommand.ExecuteAsync(null);

        Assert.Equal(1, simBrief.Resets);
        Assert.Equal("EDDF", store.Plan.From);
    }

    [Fact]
    public async Task A_fetch_that_found_nothing_says_so_and_leaves_the_plan_alone()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, _) = Open();
        main.FlightPlan.Route = "MY ROUTE";
        simBrief.Answer = null;
        bool imported = false;
        main.FlightPlan.Imported += (_, _) => imported = true;

        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null);

        Assert.Equal("MY ROUTE", main.FlightPlan.Route);
        Assert.False(string.IsNullOrEmpty(main.FlightPlan.Status));
        Assert.False(imported);
    }

    [Fact]
    public async Task A_fetch_that_worked_says_which_route_it_brought()
    {
        (MainViewModel main, _, _) = Open();

        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null);

        Assert.Equal("Imported EDDF → EGLL", main.FlightPlan.Status);
        Assert.Equal("EGLL", main.FlightPlan.To);
    }

    [Fact]
    public async Task The_callsign_filed_with_SimBrief_replaces_the_shown_one_unless_it_gave_none()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, _) = Open();
        main.FlightPlan.Callsign = "HB-TDX";

        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null);
        Assert.Equal("DLH123", main.FlightPlan.Callsign);

        simBrief.Answer = simBrief.Answer! with { Callsign = "" };
        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null);
        Assert.Equal("DLH123", main.FlightPlan.Callsign);
    }

    [Fact]
    public async Task The_prompt_started_by_the_strip_commits_after_the_name_is_given()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, _) = Open(simbriefUser: null);

        await main.FlightPlanLoad.ToggleCommand.ExecuteAsync(null);
        SimbriefPromptViewModel prompt = Assert.IsType<SimbriefPromptViewModel>(main.Overlay);
        prompt.Username = "pilot";
        await prompt.ImportCommand.ExecuteAsync(null);

        Assert.Equal([true], simBrief.Commits);
    }

    [Fact]
    public async Task The_prompt_started_by_the_tab_only_shows_the_plan()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, _) = Open(simbriefUser: null);

        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null);
        SimbriefPromptViewModel prompt = Assert.IsType<SimbriefPromptViewModel>(main.Overlay);
        prompt.Username = "pilot";
        await prompt.ImportCommand.ExecuteAsync(null);

        Assert.Equal([false], simBrief.Commits);
    }

    // ---- the tab follows the live plan

    [Fact]
    public void The_tab_follows_the_live_plan_while_nobody_edits_it()
    {
        (MainViewModel main, _, MovingFlightPlanStore store) = Open();

        store.Plan = store.Plan with { Callsign = "HB-ABC", Type = "C172" }; // the simulator reported them
        main.FlightPlan.Refresh();

        Assert.Equal("HB-ABC", main.FlightPlan.Callsign);
        Assert.Equal("C172", main.FlightPlan.Type);
    }

    [Fact]
    public void An_edit_is_not_overwritten_by_the_live_plan_until_it_is_saved()
    {
        (MainViewModel main, _, MovingFlightPlanStore store) = Open();
        main.FlightPlan.Route = "MINE";

        store.Plan = store.Plan with { Route = "THEIRS" };
        main.FlightPlan.Refresh();
        Assert.Equal("MINE", main.FlightPlan.Route);

        main.FlightPlan.SaveCommand.Execute(null);
        store.Plan = store.Plan with { Route = "LATER" };
        main.FlightPlan.Refresh();
        Assert.Equal("LATER", main.FlightPlan.Route);
    }

    [Fact]
    public void Save_shows_the_plan_as_it_was_stored()
    {
        (MainViewModel main, _, MovingFlightPlanStore store) = Open();
        store.OwnCallsign = "HB-OWN";

        main.FlightPlan.Callsign = "";
        main.FlightPlan.From = "LSZH";
        main.FlightPlan.SaveCommand.Execute(null);

        Assert.Equal("HB-OWN", main.FlightPlan.Callsign); // the aircraft's own, filled in by the app
        Assert.Equal("LSZH", store.Saved[^1].From);
    }

    [Fact]
    public void Clear_forgets_the_fetch_and_is_kept_until_saved()
    {
        (MainViewModel main, ScriptedSimBrief simBrief, MovingFlightPlanStore store) = Open();
        store.Plan = new FlightPlanData("DLH123", "A320", "IFR", "EDDF", "EGLL", "", "ANEKI", "");
        main.FlightPlan.Refresh();

        main.FlightPlan.ClearCommand.Execute(null);
        main.FlightPlan.Refresh(); // the live plan is still the old one, but the cleared fields are not thrown away

        Assert.Equal(1, simBrief.Resets);
        Assert.Equal("", main.FlightPlan.From);
        Assert.Equal("EDDF", store.Plan.From);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task A_committed_import_is_not_an_unsaved_edit()
    {
        (MainViewModel main, _, MovingFlightPlanStore store) = Open();

        await main.FlightPlanLoad.ToggleCommand.ExecuteAsync(null); // the strip commits
        store.Plan = new FlightPlanData("DLH123", "A320", "IFR", "EDDF", "EGLL", "FL350", "ANEKI UL607 DVR", "RMK/TEST") with { Route = "FROM THE APP" };
        main.FlightPlan.Refresh();

        Assert.Equal("FROM THE APP", main.FlightPlan.Route);
    }

    [Fact]
    public async Task An_import_to_be_checked_is_an_unsaved_edit()
    {
        (MainViewModel main, _, MovingFlightPlanStore store) = Open();

        await main.FlightPlan.ImportFromSimbriefCommand.ExecuteAsync(null); // shown only
        store.Plan = store.Plan with { Route = "FROM THE APP" };
        main.FlightPlan.Refresh();

        Assert.Equal("ANEKI UL607 DVR", main.FlightPlan.Route);
    }
}
