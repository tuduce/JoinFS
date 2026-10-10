using JoinFS.Live;
using JoinFS.Net;
using JoinFS.UI.Models;

namespace JoinFS.Tests.Live;

// The Live* classes carry the new UI's view of the real app. They need a running Main, so only their
// decisions are tested here, not their reads and writes of Settings.
public class LiveAdapterTests
{
    [Theory]
    [InlineData("26.6.0", "26.7.0", true)]
    [InlineData("26.6.0", "26.6.1", true)]
    [InlineData("26.6.0", "26.6.0", false)]
    [InlineData("26.7.0", "26.6.9", false)]
    [InlineData("26.6.0", " 26.7.0\r\n", true)] // the version file ends in a newline
    [InlineData("26.6.0", "", false)] // not fetched yet
    [InlineData("26.6.0", "<html>", false)]
    [InlineData("0.0.0", "26.6", true)]
    public void A_release_is_newer_only_when_its_version_is_above_ours(string current, string latest, bool expected)
    {
        Assert.Equal(expected, LiveUpdateChecker.IsNewer(current, latest));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("M", false)]
    [InlineData("Mav", true)]
    [InlineData("HB-TDX", true)]
    public void Setup_is_wanted_until_the_nickname_has_two_characters(string nickname, bool onboarded)
    {
        Assert.Equal(onboarded, LiveSettingsStore.IsOnboarded(nickname));
    }

    [Fact]
    public void The_address_book_list_starts_with_the_built_in_global_entry()
    {
        var (entries, selected) = LiveAddressBookStore.Compose([("Planet FsHub", "fshub.io:24192"), ("Mine", "10.0.0.2")], "Mine");

        Assert.Equal(["Global", "Planet FsHub", "Mine"], entries.Select(e => e.Name));
        Assert.True(entries[0].BuiltIn);
        Assert.All(entries.Skip(1), e => Assert.False(e.BuiltIn));
        Assert.Equal("Mine", selected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1.2.3.4:6809")] // an address typed into the old combo, not a name in the book
    [InlineData("Gone")]
    public void A_picked_name_that_is_not_in_the_book_falls_back_to_global(string picked)
    {
        var (_, selected) = LiveAddressBookStore.Compose([("Mine", "10.0.0.2")], picked);

        Assert.Equal("Global", selected);
    }

    [Fact]
    public void The_app_info_describes_this_build()
    {
        LiveAppInfo info = new();

        Assert.Equal(Main.Version, info.Version);
        Assert.Equal(Main.Name, info.SessionLabel);
        Assert.False(info.IsXPlaneBuild); // the tests build FS2024-Debug
        Assert.StartsWith("https://", info.DownloadUrl);
    }

    [Theory]
    [InlineData(SessionState.Connected, false, false, ConnectionState.Connected)]
    [InlineData(SessionState.Connecting, false, false, ConnectionState.Connecting)]
    [InlineData(SessionState.Unconnected, false, false, ConnectionState.Disconnected)]
    [InlineData(SessionState.Unconnected, false, true, ConnectionState.Connecting)] // still looking for the user to join
    [InlineData(SessionState.Unconnected, true, false, ConnectionState.Connecting)] // lost every peer and retrying
    [InlineData(SessionState.Connected, true, false, ConnectionState.Connecting)] // the retry stays orange until it succeeds
    public void The_network_button_state_is_the_old_buttons_colour(SessionState state, bool reconnecting, bool joiningUser, ConnectionState expected)
    {
        Assert.Equal(expected, LiveNetworkLink.MapState(state, reconnecting, joiningUser));
    }

    [Fact]
    public void The_match_explanation_names_the_engine_that_matched()
    {
        JoinFS.Substitution.MatchTrace trace = new() { engine = MatchingEngine.New };
        Assert.Equal(JoinFS.Resources.Strings.MatchExplain_EngineNew, LiveMatchExplanation.EngineDescription(trace));

        trace.engine = MatchingEngine.Classic;
        Assert.Equal(JoinFS.Resources.Strings.MatchExplain_EngineClassic, LiveMatchExplanation.EngineDescription(trace));

        trace.engine = null; // no engine involved, like a hand-picked match
        Assert.Equal("", LiveMatchExplanation.EngineDescription(trace));
        Assert.Equal("", LiveMatchExplanation.EngineDescription(null));
    }
}
