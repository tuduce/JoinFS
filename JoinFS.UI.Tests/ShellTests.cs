using JoinFS.UI.Models;
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

    [Fact]
    public void A_first_run_shows_the_onboarding_card_which_cannot_be_dismissed()
    {
        Rig rig = new(onboarded: false);

        OnboardingViewModel onboarding = Assert.IsType<OnboardingViewModel>(rig.Main.Overlay);
        Assert.False(onboarding.IsDismissable);
    }

    [Fact]
    public void A_returning_user_sees_no_overlay()
    {
        Assert.Null(new Rig().Main.Overlay);
    }

    [Fact]
    public void Continue_needs_a_nickname_then_remembers_it_and_closes()
    {
        Rig rig = new(onboarded: false);
        OnboardingViewModel onboarding = (OnboardingViewModel)rig.Main.Overlay!;
        Assert.False(onboarding.ContinueCommand.CanExecute(null));

        onboarding.Nickname = "   ";
        Assert.False(onboarding.ContinueCommand.CanExecute(null));

        onboarding.Nickname = " M "; // one letter is too short for the app
        Assert.False(onboarding.ContinueCommand.CanExecute(null));

        onboarding.Nickname = " Maverick ";
        Assert.True(onboarding.ContinueCommand.CanExecute(null));
        onboarding.ContinueCommand.Execute(null);

        Assert.Null(rig.Main.Overlay);
        Assert.True(rig.Settings.Onboarded);
        Assert.Equal("Maverick", rig.Settings.Nickname);
        Assert.Null(rig.Settings.SimbriefUsername);
    }

    [Fact]
    public void The_simbrief_username_is_only_kept_when_the_field_was_opened()
    {
        Rig hidden = new(onboarded: false);
        OnboardingViewModel a = (OnboardingViewModel)hidden.Main.Overlay!;
        a.Nickname = "A";
        a.SimbriefUsername = "typed-then-hidden";
        a.ContinueCommand.Execute(null);
        Assert.Null(hidden.Settings.SimbriefUsername);

        Rig shown = new(onboarded: false);
        OnboardingViewModel b = (OnboardingViewModel)shown.Main.Overlay!;
        b.Nickname = "B";
        b.ToggleSimbriefInputCommand.Execute(null);
        b.SimbriefUsername = " HBTDXnav ";
        b.ContinueCommand.Execute(null);
        Assert.Equal("HBTDXnav", shown.Settings.SimbriefUsername);
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
        Assert.Equal(["https://joinfs.net/download"], rig.Platform.OpenedUrls);

        AboutViewModel current = new(rig.Services.App, update: null, rig.Platform);
        Assert.False(current.NewVersionAvailable);
        current.OpenDownloadCommand.Execute(null);
        Assert.Single(rig.Platform.OpenedUrls);
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
