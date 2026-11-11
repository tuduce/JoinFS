using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

public class ShellTests
{
    [Fact]
    public void It_starts_collapsed_on_the_home_tab()
    {
        Rig rig = new();

        Assert.False(rig.Main.IsExpanded);
        Assert.True(rig.Main.IsCollapsed);
        Assert.Equal(TabId.Home, rig.Main.SelectedTab);
        Assert.IsType<HomeViewModel>(rig.Main.CurrentTab);
    }

    [Fact]
    public void The_sidebar_lists_the_eleven_tabs_in_order()
    {
        Rig rig = new();

        Assert.Equal(
            ["Home", "Network Hubs", "Session", "Aircraft", "Objects", "Model Matching", "Flight Plan", "Recorder", "Chat", "Monitor", "Settings"],
            rig.Main.NavItems.Select(n => n.Label));
    }

    [Theory]
    [InlineData(TabId.Network, typeof(HubsViewModel))]
    [InlineData(TabId.Session, typeof(SessionViewModel))]
    [InlineData(TabId.Aircraft, typeof(AircraftViewModel))]
    [InlineData(TabId.Objects, typeof(ObjectsViewModel))]
    [InlineData(TabId.Models, typeof(ModelMatchingViewModel))]
    [InlineData(TabId.FlightPlan, typeof(FlightPlanViewModel))]
    [InlineData(TabId.Recorder, typeof(RecorderViewModel))]
    [InlineData(TabId.Chat, typeof(ChatViewModel))]
    [InlineData(TabId.Monitor, typeof(MonitorViewModel))]
    [InlineData(TabId.Settings, typeof(SettingsViewModel))]
    public void Going_to_a_tab_shows_it_and_expands_the_window(TabId tab, Type viewModel)
    {
        Rig rig = new();

        rig.Main.NavItems.Single(n => n.Tab == tab).SelectCommand.Execute(null);

        Assert.True(rig.Main.IsExpanded);
        Assert.IsType(viewModel, rig.Main.CurrentTab);
    }

    [Fact]
    public void Exactly_the_active_tab_is_marked_active()
    {
        Rig rig = new();

        rig.Main.GoTo(TabId.Session);

        Assert.Equal([TabId.Session], rig.Main.NavItems.Where(n => n.IsActive).Select(n => n.Tab));
    }

    [Fact]
    public void Toggle_expand_flips_the_window_size_state()
    {
        Rig rig = new();

        rig.Main.ToggleExpandCommand.Execute(null);
        Assert.True(rig.Main.IsExpanded);

        rig.Main.ToggleExpandCommand.Execute(null);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void The_unread_dot_shows_in_the_sidebar_always_and_in_the_title_bar_only_while_collapsed()
    {
        Rig rig = new();
        Assert.True(rig.Main.HasNewChat);
        Assert.True(rig.Main.ShowChatBadge);
        Assert.True(rig.Main.NavItems.Single(n => n.Tab == TabId.Chat).ShowBadge);

        rig.Main.IsExpanded = true;
        Assert.False(rig.Main.ShowChatBadge);
        Assert.True(rig.Main.NavItems.Single(n => n.Tab == TabId.Chat).ShowBadge);
    }

    [Fact]
    public void Opening_chat_clears_the_unread_dot_goes_to_chat_and_expands()
    {
        Rig rig = new();

        rig.Main.OpenChatCommand.Execute(null);

        Assert.Equal(TabId.Chat, rig.Main.SelectedTab);
        Assert.True(rig.Main.IsExpanded);
        Assert.False(rig.Main.HasNewChat);
        Assert.False(rig.Main.NavItems.Single(n => n.Tab == TabId.Chat).ShowBadge);
        Assert.False(rig.Services.Chat.HasUnread);
    }

    // --- First run ---

    private static OnboardingViewModel Onboarding(Rig rig) => (OnboardingViewModel)rig.Main.Overlay!;

    private static void Next(OnboardingViewModel onboarding)
    {
        Assert.True(onboarding.NextCommand.CanExecute(null), "Next is not possible on " + onboarding.Step);
        onboarding.NextCommand.Execute(null);
    }

    /// <summary>Answers the nickname and stops on the list of hubs.</summary>
    private static OnboardingViewModel AtHubStep(Rig rig)
    {
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Assert.Equal(OnboardingStep.Hubs, onboarding.Step);
        return onboarding;
    }

    private static void Pick(OnboardingViewModel onboarding, string hub) =>
        onboarding.SelectedHub = onboarding.Hubs.Single(h => h.Name == hub);

    /// <summary>Polls as the app does, long enough for what waits to happen.</summary>
    private static void PollFor(MainViewModel main, int polls)
    {
        for (int i = 0; i < polls; i++)
            main.Poll();
    }

    [Fact]
    public void A_first_run_shows_the_onboarding_card_over_the_expanded_window_and_it_cannot_be_dismissed()
    {
        Rig rig = new(onboarded: false);

        OnboardingViewModel onboarding = Assert.IsType<OnboardingViewModel>(rig.Main.Overlay);
        Assert.False(onboarding.IsDismissable);
        Assert.Equal(OnboardingStep.Welcome, onboarding.Step);
        Assert.True(rig.Main.IsExpanded);
    }

    [Fact]
    public void A_returning_user_sees_no_overlay_and_a_collapsed_window()
    {
        Rig rig = new();

        Assert.Null(rig.Main.Overlay);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void There_are_two_steps_when_the_simulator_was_found_and_back_returns_to_the_first_with_the_answers_kept()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        Assert.False(onboarding.BackCommand.CanExecute(null));
        Assert.Equal("Step 1 of 2", onboarding.StepText);

        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Assert.Equal(OnboardingStep.Hubs, onboarding.Step);
        Assert.Equal("Step 2 of 2", onboarding.StepText);
        Assert.False(onboarding.NextCommand.CanExecute(null)); // the last step has its own buttons

        onboarding.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Welcome, onboarding.Step);
        Assert.Equal("Maverick", onboarding.Nickname);
        Assert.False(onboarding.BackCommand.CanExecute(null));
    }

    [Fact]
    public void The_nickname_needs_two_characters_before_the_next_step()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        Assert.False(onboarding.NextCommand.CanExecute(null));

        onboarding.Nickname = "   ";
        Assert.False(onboarding.NextCommand.CanExecute(null));

        onboarding.Nickname = " M "; // one letter is too short for the app
        Assert.False(onboarding.NextCommand.CanExecute(null));

        onboarding.Nickname = " Maverick ";
        Assert.True(onboarding.NextCommand.CanExecute(null));
    }

    [Fact]
    public void Nothing_is_kept_before_the_last_step_and_finishing_remembers_the_nickname_closes_the_card_and_compacts_the_window()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = " Maverick ";
        Next(onboarding);
        Assert.False(rig.Settings.Onboarded);
        Assert.Equal("", rig.Settings.Nickname);

        onboarding.SkipCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Settings.Onboarded);
        Assert.Equal("Maverick", rig.Settings.Nickname);
        Assert.Null(rig.Settings.SimbriefUsername);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void The_simbrief_username_is_asked_on_the_first_screen_only_of_a_user_of_simbrief_and_kept_only_then()
    {
        Rig no = new(onboarded: false);
        OnboardingViewModel a = Onboarding(no);
        a.Nickname = "Ace";
        Assert.False(a.UsesSimbrief);
        Assert.True(a.NoSimbrief); // the link "Do you use SimBrief?" shows until it is clicked
        a.UseSimbriefCommand.Execute(null);
        Assert.True(a.UsesSimbrief);
        a.SimbriefUsername = "typed-then-given-up";
        a.DropSimbriefCommand.Execute(null);
        Assert.False(a.UsesSimbrief);
        Next(a);
        a.SkipCommand.Execute(null);
        Assert.Null(no.Settings.SimbriefUsername);

        Rig yes = new(onboarded: false);
        OnboardingViewModel b = Onboarding(yes);
        b.Nickname = "Bob";
        b.UseSimbriefCommand.Execute(null);
        Assert.False(b.NoSimbrief);
        Assert.False(b.NextCommand.CanExecute(null)); // yes, but no name yet
        b.SimbriefUsername = " HBTDXnav ";
        Next(b);
        b.SkipCommand.Execute(null);
        Assert.Equal("HBTDXnav", yes.Settings.SimbriefUsername);
    }

    [Fact]
    public void Finishing_with_a_simbrief_username_fetches_the_flight_plan()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Bob";
        onboarding.UseSimbriefCommand.Execute(null);
        onboarding.SimbriefUsername = "HBTDXnav";
        Next(onboarding);
        Assert.Equal("", rig.Main.FlightPlan.From);

        onboarding.SkipCommand.Execute(null);

        Assert.Equal("LSZH", rig.Main.FlightPlan.From);
        Assert.Equal("LSGG", rig.Main.FlightPlan.To);
    }

    [Fact]
    public void The_simulator_folder_is_not_asked_when_the_simulator_was_found_and_the_models_are_scanned_without_asking()
    {
        Rig rig = new(onboarded: false);
        rig.Main.Simulator.ConnectAsync().GetAwaiter().GetResult();
        OnboardingViewModel onboarding = AtHubStep(rig);
        Assert.Equal(@"C:\Flight Simulator Packages", onboarding.Folder);

        onboarding.SkipCommand.Execute(null);

        FakeModelScanSource source = (FakeModelScanSource)rig.Services.ModelScan;
        (string folder, IReadOnlyList<string> subfolders, IReadOnlyList<string> addOns, IReadOnlyList<string> additional) = Assert.Single(source.Scans);
        Assert.Equal(@"C:\Flight Simulator Packages", folder);
        Assert.Equal(["Airplanes"], subfolders);
        Assert.Equal(["Aerosoft CRJ"], addOns);
        Assert.Equal([@"D:\Community"], additional);
        Assert.Empty(source.SavedFolders); // the known folder is left alone
        Assert.False(rig.Settings.ModelScanOnConnect);
    }

    [Fact]
    public void Scanning_before_the_simulator_connects_is_left_to_the_connection()
    {
        Rig rig = new(onboarded: false);

        AtHubStep(rig).SkipCommand.Execute(null);

        Assert.Empty(((FakeModelScanSource)rig.Services.ModelScan).Scans);
        Assert.True(rig.Settings.ModelScanOnConnect);
    }

    [Fact]
    public void X_plane_scans_its_folder_at_once_because_it_needs_no_connection()
    {
        Rig rig = new(onboarded: false, xplaneBuild: true);

        AtHubStep(rig).SkipCommand.Execute(null);

        FakeXPlaneScanSource source = (FakeXPlaneScanSource)rig.Services.XPlaneScan;
        (string folder, IReadOnlyList<string> aircraftFolders, IReadOnlyList<string> unlinked) = Assert.Single(source.Scans);
        Assert.Equal(@"C:\X-Plane 12", folder);
        Assert.Empty(aircraftFolders); // none named means all of them, also those installed later
        Assert.Empty(unlinked);
        Assert.False(rig.Settings.ModelScanOnConnect);
    }

    [Fact]
    public void The_folder_is_asked_between_the_nickname_and_the_hubs_only_when_the_simulator_was_not_found()
    {
        Rig rig = new(onboarded: false, simulatorFound: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        Assert.Equal("Step 1 of 3", onboarding.StepText);
        onboarding.Nickname = "Maverick";

        Next(onboarding);
        Assert.Equal(OnboardingStep.Simulator, onboarding.Step);
        Assert.Equal("Step 2 of 3", onboarding.StepText);
        Assert.True(onboarding.HasFolderHint);
        Assert.Equal(rig.Services.ModelScan.FolderPrompt, onboarding.FolderHint);

        onboarding.Folder = @" D:\MSFS\Packages ";
        Next(onboarding);
        Assert.Equal(OnboardingStep.Hubs, onboarding.Step);
        Assert.Equal("Step 3 of 3", onboarding.StepText);
        onboarding.BackCommand.Execute(null);
        Assert.Equal(OnboardingStep.Simulator, onboarding.Step);
        Next(onboarding);

        onboarding.SkipCommand.Execute(null);

        FakeModelScanSource source = (FakeModelScanSource)rig.Services.ModelScan;
        Assert.Equal([@"D:\MSFS\Packages"], source.SavedFolders);
        Assert.True(rig.Settings.ModelScanOnConnect); // the simulator is not connected: the scan waits for it
    }

    [Fact]
    public void Without_a_simulator_folder_there_is_nothing_to_scan_but_the_user_can_still_finish()
    {
        Rig rig = new(onboarded: false, simulatorFound: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Next(onboarding); // the folder left empty
        onboarding.SkipCommand.Execute(null);

        Assert.True(rig.Settings.Onboarded);
        Assert.Empty(((FakeModelScanSource)rig.Services.ModelScan).SavedFolders);
        Assert.Empty(((FakeModelScanSource)rig.Services.ModelScan).Scans);
        Assert.False(rig.Settings.ModelScanOnConnect);
    }

    [Fact]
    public async Task Browse_puts_the_folder_that_was_picked_in_the_box()
    {
        Rig rig = new(onboarded: false, simulatorFound: false);
        rig.Platform.PickedFolder = @"E:\Sims\Packages";

        await Onboarding(rig).BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"E:\Sims\Packages", Onboarding(rig).Folder);
    }

    [Fact]
    public void X_plane_refuses_a_folder_that_is_not_an_installation_and_needs_no_hint_about_which_folder()
    {
        Rig rig = new(onboarded: false, xplaneBuild: true, simulatorFound: false);
        ((FakeXPlaneScanSource)rig.Services.XPlaneScan).InvalidFolders.Add(@"D:\Documents");
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Maverick";
        Next(onboarding);

        Assert.Equal(OnboardingStep.Simulator, onboarding.Step);
        Assert.False(onboarding.HasFolderHint);

        onboarding.Folder = @"D:\Documents";
        Assert.True(onboarding.HasFolderError);
        Assert.Contains("This is not an X-Plane folder", onboarding.FolderError);
        Assert.False(onboarding.NextCommand.CanExecute(null));

        onboarding.Folder = @"D:\Games\X-Plane 11";
        Assert.False(onboarding.HasFolderError);
        Assert.True(onboarding.NextCommand.CanExecute(null));
    }

    [Fact]
    public void The_hub_step_lists_the_online_hubs_the_busiest_first_and_leaves_out_offline_and_ignored_ones()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtHubStep(rig);

        Assert.Equal(
            ["Planet FsHub", "AirSherpa", "DigitalThemePark", "swiss and europe", "Aidan's Hub", "Flight Unlimited Network"],
            onboarding.Hubs.Select(h => h.Name));
        Assert.True(onboarding.HasHubs);
        Assert.Null(onboarding.SelectedHub);
        Assert.False(onboarding.ConnectCommand.CanExecute(null)); // nothing picked
    }

    [Fact]
    public void The_hub_list_sorts_by_the_column_that_is_clicked_and_keeps_the_pick()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtHubStep(rig);
        Pick(onboarding, "AirSherpa");
        Assert.Equal("Users ↓", onboarding.UsersColumn.Header); // the busiest first to begin with

        onboarding.NameColumn.SortCommand.Execute(null);
        Assert.Equal(
            ["Aidan's Hub", "AirSherpa", "DigitalThemePark", "Flight Unlimited Network", "Planet FsHub", "swiss and europe"],
            onboarding.Hubs.Select(h => h.Name));
        Assert.Equal("Name ↑", onboarding.NameColumn.Header);
        Assert.Equal("Users", onboarding.UsersColumn.Header);

        onboarding.NameColumn.SortCommand.Execute(null);
        Assert.Equal("swiss and europe", onboarding.Hubs[0].Name);

        onboarding.AircraftColumn.SortCommand.Execute(null); // counts begin with the highest
        Assert.Equal(["Planet FsHub", "AirSherpa", "DigitalThemePark", "swiss and europe"], onboarding.Hubs.Take(4).Select(h => h.Name));

        onboarding.StatusColumn.SortCommand.Execute(null);
        Assert.Equal(onboarding.Hubs.Select(h => h.Status).Order(StringComparer.CurrentCulture), onboarding.Hubs.Select(h => h.Status));

        Assert.Equal("AirSherpa", onboarding.SelectedHub?.Name);
        onboarding.Refresh(); // the sort holds when the list is read again
        Assert.Equal(onboarding.Hubs.Select(h => h.Status).Order(StringComparer.CurrentCulture), onboarding.Hubs.Select(h => h.Status));
    }

    [Fact]
    public void A_hub_picked_in_the_list_keeps_the_pick_when_the_list_is_read_again()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtHubStep(rig);
        Pick(onboarding, "AirSherpa");
        OnboardingHubRow row = onboarding.SelectedHub!;

        onboarding.Refresh();

        Assert.Same(row, onboarding.SelectedHub);
        Assert.True(onboarding.ConnectCommand.CanExecute(null));
    }

    [Fact]
    public void Connect_finishes_the_card_and_picks_the_hub_in_the_address_book()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtHubStep(rig);
        Pick(onboarding, "AirSherpa");

        onboarding.ConnectCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Settings.Onboarded);
        Assert.False(rig.Main.IsExpanded);
        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
        Assert.Contains(rig.Main.AddressBook.Entries, e => e.Name == "AirSherpa");
    }

    [Fact]
    public async Task The_hub_is_joined_after_the_simulator_is_connected_and_the_models_are_scanned()
    {
        Rig rig = new(onboarded: false);
        await rig.Main.Simulator.ConnectAsync();
        OnboardingViewModel onboarding = AtHubStep(rig);
        Pick(onboarding, "AirSherpa");

        onboarding.ConnectCommand.Execute(null);

        // the scan was asked for as the card closed; the network waits a moment for it
        Assert.Single(((FakeModelScanSource)rig.Services.ModelScan).Scans);
        Assert.True(rig.Main.Network.IsDisconnected);
        PollFor(rig.Main, 20);
        Assert.True(rig.Main.Network.IsConnected);
        Assert.Equal("AirSherpa", rig.Main.JoinedHubName);
    }

    [Fact]
    public void With_the_simulator_not_connected_the_hub_is_left_to_the_automatic_join_of_the_app()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtHubStep(rig);
        Pick(onboarding, "AirSherpa");

        onboarding.ConnectCommand.Execute(null);
        PollFor(rig.Main, 20);

        Assert.True(rig.Main.Network.IsDisconnected); // joined by the app when the simulator connects, from the pick that was saved
        Assert.Equal("AirSherpa", rig.Main.AddressBook.Selected?.Name);
    }

    [Fact]
    public void Not_now_joins_no_hub()
    {
        Rig rig = new(onboarded: false);
        rig.Main.Simulator.ConnectAsync().GetAwaiter().GetResult();
        OnboardingViewModel onboarding = AtHubStep(rig);
        Pick(onboarding, "AirSherpa");

        onboarding.SkipCommand.Execute(null);
        PollFor(rig.Main, 20);

        Assert.True(rig.Main.Network.IsDisconnected);
    }

    [Fact]
    public void The_hub_list_follows_the_directory_while_the_card_is_open()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtHubStep(rig);
        rig.Services.Hubs.SetIgnored("AirSherpa", true);
        Assert.Contains(onboarding.Hubs, h => h.Name == "AirSherpa");

        PollFor(rig.Main, 4); // the live lists are read every fourth poll

        Assert.DoesNotContain(onboarding.Hubs, h => h.Name == "AirSherpa");
    }

    // --- Status strip ---

    [Fact]
    public void What_the_app_reports_without_a_question_shows_in_the_strip_for_five_seconds_and_not_as_a_card()
    {
        ManualTime time = new();
        NoMessages messages = new();
        MainViewModel main = new(FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Messages = messages }, time);
        Assert.False(main.HasNotice);

        messages.Status = "Found 120 models";
        main.Poll();

        Assert.Equal("Found 120 models", main.Notice);
        Assert.True(main.HasNotice);
        Assert.False(main.NoticeIsProblem);
        Assert.IsNotType<MessageViewModel>(main.Overlay); // the fakes may offer an update card, but the count is no message

        time.Advance(TimeSpan.FromSeconds(4.9));
        main.Poll();
        Assert.True(main.HasNotice);

        time.Advance(TimeSpan.FromSeconds(0.2));
        main.Poll();
        Assert.False(main.HasNotice);
        Assert.Null(main.Notice);
    }

    [Fact]
    public void A_newer_status_replaces_the_one_showing_and_starts_the_five_seconds_again_and_dismiss_clears_it()
    {
        ManualTime time = new();
        NoMessages messages = new();
        MainViewModel main = new(FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Messages = messages }, time);
        messages.Status = "Found 120 models";
        main.Poll();
        time.Advance(TimeSpan.FromSeconds(4));
        messages.Status = "Found 121 models";
        main.Poll();
        time.Advance(TimeSpan.FromSeconds(4));
        main.Poll();
        Assert.Equal("Found 121 models", main.Notice);

        main.DismissNoticeCommand.Execute(null);

        Assert.False(main.HasNotice);
    }

    // --- About ---

    [Fact]
    public void The_version_opens_about_and_close_dismisses_it()
    {
        Rig rig = new();

        rig.Main.OpenAboutCommand.Execute(null);

        AboutViewModel about = Assert.IsType<AboutViewModel>(rig.Main.Overlay);
        Assert.Equal("Version 26.6.0", about.VersionText);
        about.CloseCommand.Execute(null);
        Assert.Null(rig.Main.Overlay);
    }

    [Fact]
    public void About_offers_the_download_only_when_a_newer_version_exists()
    {
        Rig rig = new();
        rig.Main.OpenAboutCommand.Execute(null);
        AboutViewModel withUpdate = (AboutViewModel)rig.Main.Overlay!;
        Assert.True(withUpdate.NewVersionAvailable);
        Assert.Equal("Download v26.7.0 →", withUpdate.DownloadText);
        withUpdate.OpenDownloadCommand.Execute(null);
        Assert.Equal(["https://github.com/tuduce/JoinFS/releases"], rig.Platform.OpenedUrls);

        AboutViewModel current = new(rig.Services.App, update: null, rig.Platform);
        Assert.False(current.NewVersionAvailable);
        current.OpenDownloadCommand.Execute(null);
        Assert.Single(rig.Platform.OpenedUrls);
    }

    [Fact]
    public void A_newer_version_is_announced_once_and_closing_the_card_leaves_a_reminder()
    {
        Rig rig = new();
        Assert.False(rig.Main.HasUpdateReminder);

        rig.Main.Poll();

        UpdateAvailableViewModel card = Assert.IsType<UpdateAvailableViewModel>(rig.Main.Overlay);
        Assert.Equal("Download v26.7.0 →", card.DownloadText);
        Assert.False(rig.Main.HasUpdateReminder); // while the card is up, it is the announcement
        card.CloseCommand.Execute(null);
        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Main.HasUpdateReminder);

        rig.Main.Poll();
        Assert.Null(rig.Main.Overlay); // once per run
        Assert.Empty(rig.Platform.OpenedUrls);
    }

    [Fact]
    public void The_update_reminder_shows_in_the_title_bar_only_while_collapsed()
    {
        Rig rig = new();
        rig.Main.Poll();
        rig.Main.Overlay!.CloseCommand.Execute(null);

        Assert.True(rig.Main.ShowUpdateBadge);
        rig.Main.IsExpanded = true;
        Assert.False(rig.Main.ShowUpdateBadge);
        Assert.True(rig.Main.HasUpdateReminder); // the sidebar's one stays
    }

    [Fact]
    public void The_update_card_waits_for_the_card_that_is_open_and_its_download_opens_the_release_page()
    {
        Rig rig = new(onboarded: false);

        rig.Main.Poll();
        Assert.IsType<OnboardingViewModel>(rig.Main.Overlay);

        rig.Main.Overlay!.CloseCommand.Execute(null);
        rig.Main.Poll();
        UpdateAvailableViewModel card = Assert.IsType<UpdateAvailableViewModel>(rig.Main.Overlay);

        card.DownloadCommand.Execute(null);

        Assert.Equal(["https://github.com/tuduce/JoinFS/releases"], rig.Platform.OpenedUrls);
        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Main.HasUpdateReminder);
    }

    [Fact]
    public void About_opens_the_documentation_in_the_browser()
    {
        Rig rig = new();
        rig.Main.OpenAboutCommand.Execute(null);

        ((AboutViewModel)rig.Main.Overlay!).OpenDocumentationCommand.Execute(null);

        Assert.Equal(["https://github.com/tuduce/JoinFS/wiki"], rig.Platform.OpenedUrls);
    }

    [Fact]
    public void A_newer_overlay_replaces_the_one_before_and_the_old_one_closing_leaves_the_new_one()
    {
        Rig rig = new();
        rig.Main.OpenAboutCommand.Execute(null);
        OverlayViewModel first = rig.Main.Overlay!;

        rig.Main.ShowOverlay(new ScanModelsViewModel(rig.Services.ModelScan, false, rig.Platform));
        OverlayViewModel second = rig.Main.Overlay!;
        first.Close();

        Assert.Same(second, rig.Main.Overlay);
    }
}
