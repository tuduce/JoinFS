using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoinFS.UI.Models;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.Views;

namespace JoinFS.UI.Tests;

/// <summary>
/// Draws every screen in a headless window. This is where a wrong binding path, a missing resource or a template
/// that does not resolve shows up: Avalonia only logs those at run time, so the tests listen for them.
/// Set JOINFS_UI_SCREENSHOTS to a folder to also keep a PNG of each screen.
/// </summary>
public class RenderTests
{
    private static readonly string? ScreenshotFolder = Environment.GetEnvironmentVariable("JOINFS_UI_SCREENSHOTS");

    private sealed class Problems : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) =>
            level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property or LogArea.Control or LogArea.Layout or LogArea.Visual;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Messages.Add($"{area}: {messageTemplate}");

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Messages.Add($"{area}: {Format(messageTemplate, propertyValues)}");

        private static string Format(string template, object?[] values)
        {
            foreach (object? value in values)
            {
                int open = template.IndexOf('{');
                int close = template.IndexOf('}');
                if (open < 0 || close < open)
                    break;
                template = template[..open] + value + template[(close + 1)..];
            }
            return template;
        }
    }

    private static (MainWindow Window, MainViewModel Main, Rig Rig) Open(bool expanded, bool onboarded = true, bool xplaneBuild = false)
    {
        Rig rig = new(onboarded, xplaneBuild: xplaneBuild);
        MainWindow window = new() { DataContext = rig.Main };
        if (expanded)
        {
            rig.Main.IsExpanded = true;
            window.Width = 1200;
            window.Height = 760;
        }
        window.Show();
        Settle();
        return (window, rig.Main, rig);
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Snapshot(MainWindow window, string name)
    {
        Settle();
        if (ScreenshotFolder is null)
            return;
        Directory.CreateDirectory(ScreenshotFolder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(ScreenshotFolder, name + ".png"));
    }

    private static T Find<T>(MainWindow window) where T : Visual =>
        window.GetVisualDescendants().OfType<T>().FirstOrDefault()
        ?? throw new Xunit.Sdk.XunitException($"{typeof(T).Name} is not on screen");

    [AvaloniaFact]
    public void The_collapsed_window_shows_the_strip_and_the_link_to_open_the_full_view()
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, _, _) = Open(expanded: false);

        Find<CollapsedView>(window);
        Assert.DoesNotContain(window.GetVisualDescendants().OfType<ExpandedView>(), v => v.IsEffectivelyVisible);
        Snapshot(window, "collapsed");

        Assert.Empty(problems.Messages);
        window.Close();
    }

    [AvaloniaTheory]
    [InlineData(TabId.Home)]
    [InlineData(TabId.Network)]
    [InlineData(TabId.Session)]
    [InlineData(TabId.Aircraft)]
    [InlineData(TabId.Objects)]
    [InlineData(TabId.Models)]
    [InlineData(TabId.FlightPlan)]
    [InlineData(TabId.Recorder)]
    [InlineData(TabId.Chat)]
    [InlineData(TabId.Monitor)]
    [InlineData(TabId.Settings)]
    public async Task Every_tab_renders_its_own_view_without_binding_errors(TabId tab)
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, main, _) = Open(expanded: true);
        main.Hubs.Refresh();

        main.GoTo(tab);
        Settle();

        ContentControl content = window.GetVisualDescendants().OfType<ContentControl>().First(c => ReferenceEquals(c.Content, main.CurrentTab));
        Assert.NotNull(content.GetVisualDescendants().OfType<UserControl>().FirstOrDefault());
        Snapshot(window, $"tab-{tab}");

        Assert.Empty(problems.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Expanded_rows_and_every_settings_card_render_without_binding_errors()
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, main, _) = Open(expanded: true);
        main.Hubs.Refresh();

        main.GoTo(TabId.Network);
        main.Hubs.Rows[0].ToggleExpandedCommand.Execute(null);
        Snapshot(window, "hubs-expanded");

        main.GoTo(TabId.Session);
        main.Session.Rows[1].ToggleExpandedCommand.Execute(null);
        main.Session.Rows[1].CockpitEntry = true;
        Snapshot(window, "session-expanded");

        main.GoTo(TabId.Aircraft);
        main.Aircraft.Rows[1].ToggleExpandedCommand.Execute(null);
        Snapshot(window, "aircraft-expanded");

        main.GoTo(TabId.Objects);
        main.Objects.ListIgnoredObjects = true;
        main.Objects.Rows[0].SelectCommand.Execute(null);
        main.Objects.Rows[0].Actions[1].Command.Execute(null);
        Snapshot(window, "objects-expanded");

        main.GoTo(TabId.Settings);
        foreach (var section in main.Settings.Sections)
        {
            section.ToggleCommand.Execute(null);
            Snapshot(window, $"settings-{section.Title.Split(' ')[0]}");
        }

        Assert.Empty(problems.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public void Every_overlay_renders_on_top_of_the_window_without_binding_errors()
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, main, rig) = Open(expanded: true);
        main.GoTo(TabId.Aircraft);

        (string Name, OverlayViewModel Overlay)[] overlays =
        [
            ("onboarding", new OnboardingViewModel(main.Profile)),
            ("password", new PasswordPromptViewModel("Aidan's Hub", _ => Task.CompletedTask)),
            ("simbrief", new SimbriefPromptViewModel(_ => Task.CompletedTask)),
            ("substitute", new SubstituteViewModel(new JoinFS.UI.Models.ModelTarget("GC1a Swift (Factory)"), new JoinFS.UI.Models.ModelChoice("GC1a Swift - factory", "Factory"), rig.Services.Models)),
            ("height", new AdjustHeightViewModel("GC1a Swift (Factory)", main.Profile)),
            ("explain", new ExplainMatchViewModel(new JoinFS.UI.Models.MatchExplanation("A320", "Result: Default - matched 'GC1a Swift'", "The ICAO type was guessed from the title.", [new JoinFS.UI.Models.ExplainRow("Category", "SingleProp", "SingleProp (+60)", true), new JoinFS.UI.Models.ExplainRow("Livery", "Default", "Closest available")], ["1. Exact title match - not found.", "2. Category fallback - matched."], "Models come from the simulator.", "# Match Report - A320"), rig.Services.Models, rig.Platform)),
            ("variables", new VariablesOverlayViewModel("PMDG 777-200ER GE PMDG House", ["Custom_FMC_Vars", "ListBox_Sets"], rig.Platform)),
            ("scan", new ScanModelsViewModel(false, rig.Platform)),
            ("about", new AboutViewModel(rig.Services.App, rig.Services.Updates.CheckForUpdate(), rig.Platform)),
            ("xplane-plugin", new InstallXPlanePluginViewModel(rig.Services.XPlanePlugin, rig.Platform)),
        ];

        foreach ((string name, OverlayViewModel overlay) in overlays)
        {
            main.ShowOverlay(overlay);
            Settle();
            Assert.True(main.IsOverlayOpen, name);
            Snapshot(window, $"overlay-{name}");
        }

        Assert.Empty(problems.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public void The_xplane_build_shows_its_settings_card_and_label_options()
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, main, _) = Open(expanded: true, xplaneBuild: true);

        main.GoTo(TabId.Settings);
        main.Settings.XPlane.ToggleCommand.Execute(null);
        Snapshot(window, "settings-xplane-build");
        main.Settings.Simulator.ToggleCommand.Execute(null);
        Snapshot(window, "settings-xplane-build-simulator");

        main.Settings.Simulator.OpenModelScanningCommand.Execute(null);
        ((ScanXPlaneModelsViewModel)main.Overlay!).GenerateCsl = true;
        Snapshot(window, "overlay-scan-xplane");

        Assert.Empty(problems.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public async Task A_joined_hub_that_is_not_in_the_book_shows_as_the_pickers_text()
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, main, _) = Open(expanded: true);

        await main.Hubs.Rows.Single(r => r.Name == "Flight Unlimited Network").JoinCommand.ExecuteAsync(null);
        Settle();
        Snapshot(window, "picker-joined-hub");

        ComboBox picker = window.GetVisualDescendants().OfType<ComboBox>().First(c => c.IsEffectivelyVisible);
        Assert.Equal("Flight Unlimited Network", picker.PlaceholderText);
        Assert.Null(picker.SelectedItem);
        Assert.DoesNotContain(picker.Items.Cast<object>(), i => i is JoinFS.UI.ViewModels.AddressBookRow { Name: "Flight Unlimited Network" });

        main.IsExpanded = false;
        Settle();
        Snapshot(window, "picker-joined-hub-collapsed");

        Assert.Empty(problems.Messages);
        window.Close();
    }

    [AvaloniaFact]
    public void The_first_run_card_shows_over_the_collapsed_window()
    {
        Problems problems = new();
        Logger.Sink = problems;
        var (window, main, _) = Open(expanded: false, onboarded: false);

        Assert.IsType<OnboardingViewModel>(main.Overlay);
        Snapshot(window, "onboarding-collapsed");

        Assert.Empty(problems.Messages);
        window.Close();
    }
}
