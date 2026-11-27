using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;
using JoinFS.UI.Views;
using JoinFS.UI.Views.Controls;
using JoinFS.UI.Views.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>The Network Hubs, Session, Aircraft and Monitor tabs can be shown in windows of their own, apart from the main window.</summary>
public class UndockTests
{
    [Theory]
    [InlineData(TabId.Network, typeof(HubsViewModel))]
    [InlineData(TabId.Session, typeof(SessionViewModel))]
    [InlineData(TabId.Aircraft, typeof(AircraftViewModel))]
    [InlineData(TabId.Monitor, typeof(MonitorViewModel))]
    public void An_undocked_tab_gets_a_window_and_the_main_window_says_where_it_went(TabId tab, Type viewModel)
    {
        Rig rig = new();
        rig.Main.GoTo(tab);

        rig.Main.Undock(tab);

        TabWindowViewModel window = Assert.Single(rig.Main.TabWindows);
        Assert.Equal(tab, window.Tab);
        Assert.IsType(viewModel, window.Content);
        Assert.True(rig.Main.IsUndocked(tab));
        UndockedTabViewModel placeholder = Assert.IsType<UndockedTabViewModel>(rig.Main.CurrentTab);
        Assert.Contains(rig.Main.NavItems.Single(n => n.Tab == tab).Label, placeholder.Message);
    }

    [Theory]
    [InlineData(TabId.Home)]
    [InlineData(TabId.Objects)]
    [InlineData(TabId.Models)]
    [InlineData(TabId.FlightPlan)]
    [InlineData(TabId.Recorder)]
    [InlineData(TabId.Chat)]
    [InlineData(TabId.Settings)]
    public void The_other_tabs_stay_in_the_main_window(TabId tab)
    {
        Rig rig = new();

        rig.Main.Undock(tab);

        Assert.Empty(rig.Main.TabWindows);
        Assert.False(MainViewModel.CanUndock(tab));
    }

    [Fact]
    public void Undocking_a_tab_that_has_a_window_brings_that_window_to_the_front()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Session);
        TabWindowViewModel window = rig.Main.TabWindows.Single();
        int activated = 0;
        window.ActivateRequested += (_, _) => activated++;

        rig.Main.Undock(TabId.Session);
        rig.Main.GoTo(TabId.Session); // the sidebar row

        Assert.Same(window, Assert.Single(rig.Main.TabWindows));
        Assert.Equal(2, activated);
    }

    [Fact]
    public void Put_back_shows_the_tab_in_the_main_window_again()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Aircraft);
        Assert.True(rig.Main.IsCollapsed);

        rig.Main.TabWindows.Single().DockCommand.Execute(null);

        Assert.Empty(rig.Main.TabWindows);
        Assert.True(rig.Main.IsExpanded);
        Assert.Equal(TabId.Aircraft, rig.Main.SelectedTab);
        Assert.IsType<AircraftViewModel>(rig.Main.CurrentTab);
    }

    [Fact]
    public void Closing_the_window_puts_the_tab_back_and_leaves_the_main_window_as_it_is()
    {
        Rig rig = new();
        rig.Main.GoTo(TabId.Home);
        rig.Main.Undock(TabId.Monitor);
        rig.Main.ToggleExpandCommand.Execute(null);

        rig.Main.TabWindows.Single().OnClosed();

        Assert.Empty(rig.Main.TabWindows);
        Assert.True(rig.Main.IsCollapsed);
        Assert.Equal(TabId.Home, rig.Main.SelectedTab);
    }

    [Fact]
    public void An_undocked_tab_is_read_while_the_main_window_is_collapsed_or_shows_another_tab()
    {
        ScriptedSession source = new();
        AppServices services = Services.Fake.FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Session = source };
        MainViewModel main = new(services);

        main.Undock(TabId.Session);
        int opened = source.Reads;
        Assert.True(opened > 0); // the window opens on what is true now

        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(opened + 2, source.Reads); // collapsed, as often as when it is shown in the main window

        main.GoTo(TabId.Aircraft);
        int now = source.Reads;
        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(now + 2, source.Reads);

        // once back in the main window, it is read only while it is shown there
        main.TabWindows.Single().OnClosed();
        now = source.Reads;
        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(now, source.Reads);
    }

    [Fact]
    public void A_card_an_undocked_tab_opens_goes_over_its_window_and_not_over_the_main_window()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Aircraft);
        TabWindowViewModel window = rig.Main.TabWindows.Single();
        AdjustHeightViewModel card = new(new HeightAdjustment("GC1a Swift (Factory)", 15), _ => { });

        new TabShell(rig.Main, TabId.Aircraft).ShowOverlay(card);

        Assert.Same(card, window.Overlay);
        Assert.True(window.IsOverlayOpen);
        Assert.Null(rig.Main.MainOverlay);
        Assert.False(rig.Main.IsMainOverlayOpen);
        Assert.True(rig.Main.IsOverlayOpen); // still the one card of the app: messages wait for it

        card.Close();
        Assert.Null(window.Overlay);
        Assert.False(rig.Main.IsOverlayOpen);
    }

    [Fact]
    public void A_card_of_a_docked_tab_or_of_the_app_goes_over_the_main_window()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Session);
        TabWindowViewModel window = rig.Main.TabWindows.Single();

        MessageViewModel message = new(null, "Hello");
        rig.Main.ShowOverlay(message);
        Assert.Same(message, rig.Main.MainOverlay);
        Assert.Null(window.Overlay);
        message.Close();

        AdjustHeightViewModel card = new(new HeightAdjustment("GC1a Swift (Factory)", 15), _ => { });
        new TabShell(rig.Main, TabId.Aircraft).ShowOverlay(card);
        Assert.Same(card, rig.Main.MainOverlay);
        Assert.Null(window.Overlay);
    }

    [Fact]
    public async Task A_password_asked_by_the_undocked_hubs_tab_goes_over_its_window()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Network);
        TabWindowViewModel window = rig.Main.TabWindows.Single();

        await new TabShell(rig.Main, TabId.Network).JoinAsync(new AddressBookEntry("Club", "club.example", RequiresPassword: true));

        Assert.IsType<PasswordPromptViewModel>(window.Overlay);
        Assert.Null(rig.Main.MainOverlay);
    }

    [Fact]
    public void Closing_the_window_closes_the_card_it_shows()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Aircraft);
        AdjustHeightViewModel card = new(new HeightAdjustment("GC1a Swift (Factory)", 15), _ => { });
        new TabShell(rig.Main, TabId.Aircraft).ShowOverlay(card);

        rig.Main.TabWindows.Single().OnClosed();

        Assert.Null(rig.Main.Overlay);
    }

    // ---- where the windows were, kept between runs

    [Fact]
    public void The_places_of_the_tab_windows_read_back_as_they_were_written()
    {
        Dictionary<TabId, WindowPlace> places = new()
        {
            [TabId.Session] = new WindowPlace(-1800, 120, 960, 600),
            [TabId.Monitor] = new WindowPlace(40, 30, 700, 420),
        };

        string text = WindowPlace.Format(places);

        Assert.Equal("Session=-1800,120,960,600;Monitor=40,30,700,420", text);
        Assert.Equal(places, WindowPlace.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Session")]
    [InlineData("Session=1,2,3")]
    [InlineData("Session=1,2,x,4")]
    [InlineData("Session=1,2,0,4")]
    [InlineData("Radar=1,2,3,4")]
    [InlineData("42=1,2,3,4")]
    public void A_place_that_is_not_understood_is_left_out(string? text)
    {
        Assert.Empty(WindowPlace.Parse(text));
        Assert.Single(WindowPlace.Parse(text + ";Aircraft=1,2,3,4"));
    }

    [Fact]
    public void The_profile_saves_a_tab_window_place_and_a_new_run_reads_it()
    {
        InMemorySettingsStore store = new(new UserSettings { Onboarded = true, Nickname = "Me" });
        ProfileViewModel profile = new(store);
        Assert.Null(profile.TabWindowPlace(TabId.Aircraft));

        profile.SaveTabWindowPlace(TabId.Aircraft, new WindowPlace(10, 20, 800, 500));

        Assert.Equal(new WindowPlace(10, 20, 800, 500), new ProfileViewModel(store).TabWindowPlace(TabId.Aircraft));
    }

    [Fact]
    public void Which_tabs_are_undocked_is_saved_as_they_are_undocked_and_put_back()
    {
        Rig rig = new();

        rig.Main.Undock(TabId.Monitor);
        rig.Main.Undock(TabId.Session);
        Assert.Equal([TabId.Monitor, TabId.Session], rig.Settings.UndockedTabs);

        rig.Main.TabWindows.First(w => w.Tab == TabId.Monitor).OnClosed(); // its ✕
        Assert.Equal([TabId.Session], rig.Settings.UndockedTabs);

        rig.Main.TabWindows.Single().DockCommand.Execute(null);
        Assert.Empty(rig.Settings.UndockedTabs);
    }

    [Fact]
    public void Closing_the_app_keeps_the_undocked_tabs_and_the_next_run_undocks_them_again()
    {
        Rig rig = new();
        rig.Main.Undock(TabId.Aircraft);
        rig.Main.Undock(TabId.Network);

        // the main window closes, and its tab windows with it
        rig.Main.OnShutdown();
        foreach (TabWindowViewModel window in rig.Main.TabWindows.ToList())
            window.OnClosed();
        Assert.Equal([TabId.Aircraft, TabId.Network], rig.Settings.UndockedTabs);

        MainViewModel next = new(rig.Services);
        Assert.Empty(next.TabWindows); // not before the main window has opened
        next.RestoreUndockedTabs();
        Assert.Equal([TabId.Aircraft, TabId.Network], next.TabWindows.Select(w => w.Tab));
    }

    [Theory]
    [InlineData("Session,Monitor", new[] { TabId.Session, TabId.Monitor })]
    [InlineData(" Session , Session,Radar,,3,Home", new[] { TabId.Session, TabId.Home })]
    [InlineData("", new TabId[0])]
    [InlineData(null, new TabId[0])]
    public void The_undocked_tabs_read_back_as_they_were_written_and_unknown_names_are_left_out(string? text, TabId[] tabs)
    {
        Assert.Equal(tabs, UserSettings.ParseTabs(text));
        Assert.Equal(tabs, UserSettings.ParseTabs(UserSettings.FormatTabs(tabs)));
    }

    [Fact]
    public void A_saved_tab_that_cannot_be_undocked_stays_in_the_main_window()
    {
        Rig rig = new();
        rig.Settings.UndockedTabs = [TabId.Settings, TabId.Session];
        MainViewModel main = new(rig.Services);

        main.RestoreUndockedTabs();

        Assert.Equal([TabId.Session], main.TabWindows.Select(w => w.Tab));
    }

    // ---- in windows

    [AvaloniaFact]
    public void Tabs_undocked_when_the_app_closed_open_in_their_windows_when_it_starts_again()
    {
        Rig rig = new();
        MainWindow main = new() { DataContext = rig.Main };
        main.Show();
        rig.Main.Undock(TabId.Session);
        rig.Main.Undock(TabId.Monitor);
        Settle();
        main.Close();
        Settle();
        Assert.Empty(rig.Main.TabWindows);

        MainViewModel next = new(rig.Services);
        MainWindow nextMain = new() { DataContext = next };
        nextMain.Show();
        Settle();

        Assert.Equal([TabId.Session, TabId.Monitor], nextMain.TabWindows.Select(w => ((TabWindowViewModel)w.DataContext!).Tab));
        Assert.All(nextMain.TabWindows, w => Assert.True(w.IsVisible));
        nextMain.Close();
    }

    private sealed class Problems : ILogSink
    {
        public List<string> Messages { get; } = [];

        public bool IsEnabled(LogEventLevel level, string area) =>
            level >= LogEventLevel.Warning && area is LogArea.Binding or LogArea.Property or LogArea.Control or LogArea.Layout or LogArea.Visual;

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate) => Messages.Add($"{area}: {messageTemplate}");

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
            Messages.Add($"{area}: {messageTemplate} {string.Join(", ", propertyValues)}");
    }

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Visual root) where T : Visual =>
        root.GetVisualDescendants().OfType<T>().FirstOrDefault()
        ?? throw new Xunit.Sdk.XunitException($"{typeof(T).Name} is not on screen");

    // JOINFS_UI_SCREENSHOTS keeps a PNG of each window, as in RenderTests.
    private static void Snapshot(Window window, string name)
    {
        Settle();
        if (Environment.GetEnvironmentVariable("JOINFS_UI_SCREENSHOTS") is not { } folder)
            return;
        Directory.CreateDirectory(folder);
        window.CaptureRenderedFrame()?.Save(Path.Combine(folder, name + ".png"));
    }

    private static void Click(Window window, Control control)
    {
        Avalonia.Point middle = control.TranslatePoint(new Avalonia.Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        window.MouseDown(middle, Avalonia.Input.MouseButton.Left);
        window.MouseUp(middle, Avalonia.Input.MouseButton.Left);
        Settle();
    }

    [AvaloniaTheory]
    [InlineData(TabId.Network, typeof(HubsView))]
    [InlineData(TabId.Session, typeof(SessionView))]
    [InlineData(TabId.Aircraft, typeof(AircraftView))]
    [InlineData(TabId.Monitor, typeof(MonitorView))]
    public void The_corner_button_opens_the_tab_in_its_own_window_and_puts_it_back(TabId tab, Type view)
    {
        Problems problems = new();
        Logger.Sink = problems;
        Rig rig = new();
        MainWindow main = new() { DataContext = rig.Main, Width = 1200, Height = 760 };
        rig.Main.GoTo(tab);
        main.Show();
        Settle();

        UndockButton undock = Find<UndockButton>(main);
        Assert.Equal("Open in a separate window", ToolTip.GetTip(undock));
        Click(main, undock);

        TabWindow window = Assert.Single(main.TabWindows);
        Assert.True(window.IsVisible);
        Snapshot(main, $"undocked-{tab}-main");
        Snapshot(window, $"undocked-{tab}-window");
        Assert.IsType(view, window.GetVisualDescendants().First(v => v.GetType() == view));
        Assert.IsType<UndockedTabView>(Find<UndockedTabView>(main));
        Assert.DoesNotContain(main.GetVisualDescendants(), v => v.GetType() == view);

        UndockButton dock = Find<UndockButton>(window);
        Assert.Equal("Put back in the main window", ToolTip.GetTip(dock));
        Click(window, dock);

        Assert.Empty(main.TabWindows);
        Assert.False(window.IsVisible);
        Assert.Empty(rig.Main.TabWindows);
        Assert.Contains(main.GetVisualDescendants(), v => v.GetType() == view);

        Assert.Empty(problems.Messages);
        main.Close();
    }

    [AvaloniaFact]
    public void Closing_the_tab_window_puts_the_tab_back_and_closing_the_main_window_closes_the_tab_windows()
    {
        Rig rig = new();
        MainWindow main = new() { DataContext = rig.Main };
        main.Show();
        Settle();

        rig.Main.Undock(TabId.Session);
        rig.Main.Undock(TabId.Monitor);
        Settle();
        Assert.Equal(2, main.TabWindows.Count);

        TabWindow session = main.TabWindows.Single(w => ((TabWindowViewModel)w.DataContext!).Tab == TabId.Session);
        session.Close();
        Settle();
        Assert.False(rig.Main.IsUndocked(TabId.Session));
        TabWindow monitor = Assert.Single(main.TabWindows);

        main.Close();
        Settle();
        Assert.False(monitor.IsVisible);
        Assert.Empty(rig.Main.TabWindows);
    }

    [AvaloniaFact]
    public void A_tab_window_opens_where_and_as_big_as_it_was_left_in_an_earlier_run()
    {
        Rig rig = new();
        MainWindow main = new() { DataContext = rig.Main };
        main.Show();
        Settle();
        rig.Main.Undock(TabId.Session);
        Settle();
        TabWindow window = Assert.Single(main.TabWindows);

        window.Position = new PixelPoint(150, 90);
        window.Width = 720;
        window.Height = 450;
        Settle();
        window.Close();
        Settle();
        main.Close();

        Assert.Equal(new WindowPlace(150, 90, 720, 450), rig.Main.Profile.TabWindowPlace(TabId.Session));

        // the next run, on the same settings
        MainViewModel next = new(rig.Services);
        MainWindow nextMain = new() { DataContext = next };
        nextMain.Show();
        next.Undock(TabId.Session);
        Settle();
        TabWindow reopened = Assert.Single(nextMain.TabWindows);
        Assert.Equal(new PixelPoint(150, 90), reopened.Position);
        Assert.Equal(720, reopened.Width);
        Assert.Equal(450, reopened.Height);
        nextMain.Close();
    }

    [AvaloniaFact]
    public void A_tab_window_left_on_a_screen_that_is_gone_opens_on_this_one()
    {
        Rig rig = new();
        rig.Settings.TabWindowPlaces[TabId.Monitor] = new WindowPlace(-30000, -30000, 700, 400);
        MainViewModel main = new(rig.Services);
        MainWindow window = new() { DataContext = main };
        window.Show();

        main.Undock(TabId.Monitor);
        Settle();

        TabWindow tab = Assert.Single(window.TabWindows);
        Assert.NotEqual(new PixelPoint(-30000, -30000), tab.Position);
        Assert.Equal(960, tab.Width); // its own size, as a window opened for the first time
        window.Close();
    }

    [AvaloniaFact]
    public void A_card_of_an_undocked_tab_is_drawn_over_the_tab_window()
    {
        Problems problems = new();
        Logger.Sink = problems;
        Rig rig = new();
        MainWindow main = new() { DataContext = rig.Main };
        main.Show();
        rig.Main.Undock(TabId.Aircraft);
        Settle();
        TabWindow window = Assert.Single(main.TabWindows);

        new TabShell(rig.Main, TabId.Aircraft).ShowOverlay(new AdjustHeightViewModel(new HeightAdjustment("GC1a Swift (Factory)", 15), _ => { }));
        Settle();

        Assert.NotEmpty(window.GetVisualDescendants().OfType<Views.Overlays.AdjustHeightView>());
        Assert.Empty(main.GetVisualDescendants().OfType<Views.Overlays.AdjustHeightView>());
        Assert.Empty(problems.Messages);
        main.Close();
    }
}
