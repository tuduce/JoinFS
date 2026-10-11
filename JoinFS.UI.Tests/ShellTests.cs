using JoinFS.UI.Models;
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

    /// <summary>Answers the first three steps the shortest way and stops on the one that asks about the scan.</summary>
    private static OnboardingViewModel AtScanStep(Rig rig)
    {
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Next(onboarding);
        Next(onboarding);
        Assert.Equal(OnboardingStep.Models, onboarding.Step);
        return onboarding;
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
    public void The_steps_come_in_order_and_back_returns_to_the_one_before_with_the_answers_kept()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        Assert.False(onboarding.BackCommand.CanExecute(null));
        Assert.Equal("Step 1 of 4", onboarding.StepText);

        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Assert.Equal(OnboardingStep.SimBrief, onboarding.Step);
        Assert.Equal("Step 2 of 4", onboarding.StepText);

        Next(onboarding);
        Assert.Equal(OnboardingStep.Simulator, onboarding.Step);
        Next(onboarding);
        Assert.Equal(OnboardingStep.Models, onboarding.Step);
        Assert.False(onboarding.NextCommand.CanExecute(null)); // the last step has its own two buttons

        onboarding.BackCommand.Execute(null);
        onboarding.BackCommand.Execute(null);
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
        Next(onboarding);
        Next(onboarding);
        Assert.False(rig.Settings.Onboarded);
        Assert.Equal("", rig.Settings.Nickname);

        onboarding.SkipScanCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Settings.Onboarded);
        Assert.Equal("Maverick", rig.Settings.Nickname);
        Assert.Null(rig.Settings.SimbriefUsername);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void The_simbrief_username_is_asked_only_of_a_user_of_simbrief_and_kept_only_then()
    {
        Rig no = new(onboarded: false);
        OnboardingViewModel a = Onboarding(no);
        a.Nickname = "Ace";
        Next(a);
        Assert.False(a.UsesSimbrief);
        Assert.True(a.NoSimbrief); // "no" is what is selected until the user says otherwise
        a.UsesSimbrief = true;
        a.SimbriefUsername = "typed-then-switched-off";
        a.NoSimbrief = true;
        Assert.False(a.UsesSimbrief);
        Next(a);
        Next(a);
        a.SkipScanCommand.Execute(null);
        Assert.Null(no.Settings.SimbriefUsername);

        Rig yes = new(onboarded: false);
        OnboardingViewModel b = Onboarding(yes);
        b.Nickname = "Bob";
        Next(b);
        b.UsesSimbrief = true;
        Assert.False(b.NoSimbrief);
        Assert.False(b.NextCommand.CanExecute(null)); // yes, but no name yet
        b.SimbriefUsername = " HBTDXnav ";
        Next(b);
        Next(b);
        b.SkipScanCommand.Execute(null);
        Assert.Equal("HBTDXnav", yes.Settings.SimbriefUsername);
    }

    [Fact]
    public void The_simulator_step_shows_the_folder_that_is_known_and_what_it_is_asked_for()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);

        Assert.Equal(@"C:\Flight Simulator Packages", onboarding.Folder);
        Assert.Equal("JoinFS found your simulator in this folder. Change it if that is not right.", onboarding.FolderIntro);
        Assert.True(onboarding.HasFolderHint);
        Assert.Equal(rig.Services.ModelScan.FolderPrompt, onboarding.FolderHint);
        Assert.False(onboarding.HasFolderError);
    }

    [Fact]
    public void A_folder_the_user_chose_is_remembered_and_one_that_was_known_is_left_alone()
    {
        Rig kept = new(onboarded: false);
        AtScanStep(kept).SkipScanCommand.Execute(null);
        Assert.Empty(((FakeModelScanSource)kept.Services.ModelScan).SavedFolders);

        Rig changed = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(changed);
        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Next(onboarding);
        onboarding.Folder = @" D:\MSFS\Packages ";
        Next(onboarding);
        onboarding.SkipScanCommand.Execute(null);

        FakeModelScanSource source = (FakeModelScanSource)changed.Services.ModelScan;
        Assert.Equal([@"D:\MSFS\Packages"], source.SavedFolders);
        Assert.Empty(source.Scans);
    }

    [Fact]
    public async Task Browse_puts_the_folder_that_was_picked_in_the_box()
    {
        Rig rig = new(onboarded: false);
        rig.Platform.PickedFolder = @"E:\Sims\Packages";

        await Onboarding(rig).BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"E:\Sims\Packages", Onboarding(rig).Folder);
    }

    [Fact]
    public void X_plane_refuses_a_folder_that_is_not_an_installation_and_needs_no_hint_about_which_folder()
    {
        Rig rig = new(onboarded: false, xplaneBuild: true);
        ((FakeXPlaneScanSource)rig.Services.XPlaneScan).InvalidFolders.Add(@"D:\Documents");
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Next(onboarding);

        Assert.Equal(@"C:\X-Plane 12", onboarding.Folder);
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
    public async Task Scanning_with_the_simulator_connected_scans_with_the_choices_that_are_saved_and_compacts_the_window()
    {
        Rig rig = new(onboarded: false);
        await rig.Main.Simulator.ConnectAsync();
        OnboardingViewModel onboarding = AtScanStep(rig);
        Assert.False(onboarding.ScanWaitsForSimulator);

        onboarding.ScanCommand.Execute(null);

        FakeModelScanSource source = (FakeModelScanSource)rig.Services.ModelScan;
        (string folder, IReadOnlyList<string> subfolders, IReadOnlyList<string> addOns, IReadOnlyList<string> additional) = Assert.Single(source.Scans);
        Assert.Equal(@"C:\Flight Simulator Packages", folder);
        Assert.Equal(["Airplanes"], subfolders);
        Assert.Equal(["Aerosoft CRJ"], addOns);
        Assert.Equal([@"D:\Community"], additional);
        Assert.False(rig.Settings.ModelScanOnConnect);
        Assert.True(rig.Settings.Onboarded);
        Assert.Null(rig.Main.Overlay);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void Scanning_before_the_simulator_connects_is_left_to_the_connection()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtScanStep(rig);
        Assert.True(onboarding.ScanWaitsForSimulator);

        onboarding.ScanCommand.Execute(null);

        Assert.Empty(((FakeModelScanSource)rig.Services.ModelScan).Scans);
        Assert.True(rig.Settings.ModelScanOnConnect);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void Not_now_scans_nothing_and_still_finishes()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = AtScanStep(rig);

        onboarding.SkipScanCommand.Execute(null);

        Assert.Empty(((FakeModelScanSource)rig.Services.ModelScan).Scans);
        Assert.False(rig.Settings.ModelScanOnConnect);
        Assert.True(rig.Settings.Onboarded);
        Assert.Null(rig.Main.Overlay);
        Assert.False(rig.Main.IsExpanded);
    }

    [Fact]
    public void X_plane_scans_its_folder_at_once_because_it_needs_no_connection()
    {
        Rig rig = new(onboarded: false, xplaneBuild: true);
        OnboardingViewModel onboarding = AtScanStep(rig);
        Assert.False(onboarding.ScanWaitsForSimulator);

        onboarding.ScanCommand.Execute(null);

        FakeXPlaneScanSource source = (FakeXPlaneScanSource)rig.Services.XPlaneScan;
        (string folder, IReadOnlyList<string> aircraftFolders, IReadOnlyList<string> unlinked) = Assert.Single(source.Scans);
        Assert.Equal(@"C:\X-Plane 12", folder);
        Assert.Empty(aircraftFolders); // none named means all of them, also those installed later
        Assert.Empty(unlinked);
        Assert.False(rig.Settings.ModelScanOnConnect);
    }

    [Fact]
    public void Without_a_simulator_folder_there_is_nothing_to_scan_but_the_user_can_still_finish()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = Onboarding(rig);
        onboarding.Nickname = "Maverick";
        Next(onboarding);
        Next(onboarding);
        onboarding.Folder = "  ";
        Next(onboarding);

        Assert.True(onboarding.HasNoFolderToScan);
        Assert.False(onboarding.ScanCommand.CanExecute(null));
        onboarding.SkipScanCommand.Execute(null);

        Assert.True(rig.Settings.Onboarded);
        Assert.Empty(((FakeModelScanSource)rig.Services.ModelScan).SavedFolders);
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
