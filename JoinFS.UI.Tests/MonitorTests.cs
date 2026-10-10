using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Tests;

/// <summary>A log the test writes to, and the switches and dumps the tab asked for.</summary>
internal sealed class ScriptedMonitor : IMonitorSource
{
    public List<string> Lines { get; set; } = ["one", "two", "three"];
    public int? Fps { get; set; } = 60;
    public List<string> Files { get; set; } = [];
    public List<string> Dumps { get; } = [];
    public int Reads { get; private set; }

    public IReadOnlyList<string> GetLogLines()
    {
        Reads++;
        return [.. Lines];
    }

    public int? FramesPerSecond => Fps;
    public bool ShowNetwork { get; set; }
    public bool ShowVariables { get; set; }

    public void WriteNodeStatistics()
    {
        Dumps.Add("nodes");
        Lines.Add("== NODE STATS ==");
    }

    public void WritePacketStatistics()
    {
        Dumps.Add("packets");
        Lines.Add("== RECEIVED PACKETS ==");
    }

    public IReadOnlyList<string> LogFiles => Files;
}

public class MonitorTests
{
    private static MonitorViewModel Open(ScriptedMonitor monitor, IPlatform? platform = null) => new(monitor, platform ?? new NullPlatform());

    [Fact]
    public void The_switches_start_on_what_the_log_is_set_to()
    {
        ScriptedMonitor monitor = new() { ShowNetwork = true, ShowVariables = false };

        MonitorViewModel tab = Open(monitor);

        Assert.True(tab.LogNetwork);
        Assert.False(tab.LogVariables);
    }

    [Fact]
    public void The_network_and_variables_switches_change_what_the_log_says()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);

        tab.LogNetwork = true;
        tab.LogVariables = true;
        Assert.True(monitor.ShowNetwork);
        Assert.True(monitor.ShowVariables);

        tab.LogNetwork = false;
        Assert.False(monitor.ShowNetwork);
        Assert.True(monitor.ShowVariables);
    }

    [Fact]
    public void A_statistics_button_writes_its_dump_into_the_log()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);

        tab.WriteNodeStatisticsCommand.Execute(null);

        Assert.Equal(["nodes"], monitor.Dumps);
        Assert.Equal("== NODE STATS ==", tab.LogLines[^1]); // shown at once, not at the next poll
    }

    [Fact]
    public void A_statistics_button_can_be_used_again()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);

        tab.WritePacketStatisticsCommand.Execute(null);
        tab.WritePacketStatisticsCommand.Execute(null);

        Assert.Equal(["packets", "packets"], monitor.Dumps);
    }

    [Fact]
    public void The_log_shows_what_the_source_has()
    {
        MonitorViewModel tab = Open(new ScriptedMonitor());

        Assert.Equal(["one", "two", "three"], tab.LogLines);
    }

    [Fact]
    public void New_lines_are_added_at_the_end_without_rebuilding_the_rest()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);
        List<string> changes = [];
        tab.LogLines.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());

        monitor.Lines.Add("four");
        tab.Refresh();

        Assert.Equal(["one", "two", "three", "four"], tab.LogLines);
        Assert.Equal(["Add"], changes);
    }

    [Fact]
    public void When_the_log_scrolls_the_lines_that_went_are_taken_off_the_top()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);
        List<string> changes = [];
        tab.LogLines.CollectionChanged += (_, e) => changes.Add(e.Action.ToString());

        monitor.Lines = ["two", "three", "four", "five"]; // a window of three, two lines on
        tab.Refresh();

        Assert.Equal(["two", "three", "four", "five"], tab.LogLines);
        Assert.Equal(["Remove", "Add", "Add"], changes);
    }

    [Fact]
    public void A_log_that_has_nothing_in_common_is_shown_as_it_is()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);

        monitor.Lines = ["x", "y"];
        tab.Refresh();

        Assert.Equal(["x", "y"], tab.LogLines);
    }

    [Fact]
    public void An_unchanged_log_is_left_alone()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);
        int changes = 0;
        tab.LogLines.CollectionChanged += (_, _) => changes++;

        tab.Refresh();

        Assert.Equal(0, changes);
    }

    [Fact]
    public void The_frame_rate_is_shown_when_it_is_known_and_not_when_it_is_not()
    {
        ScriptedMonitor monitor = new();
        MonitorViewModel tab = Open(monitor);
        Assert.Equal("FPS: 60", tab.FpsText);

        monitor.Fps = null;
        tab.Refresh();
        Assert.Equal("", tab.FpsText);
    }

    [Fact]
    public void View_logs_opens_every_log_file_there_is()
    {
        ScriptedMonitor monitor = new() { Files = [@"C:\logs\log.txt", @"C:\logs\log-previous.txt"] };
        NullPlatform platform = new();
        MonitorViewModel tab = Open(monitor, platform);

        tab.ViewLogsCommand.Execute(null);

        Assert.Equal([@"C:\logs\log.txt", @"C:\logs\log-previous.txt"], platform.OpenedFiles);
        Assert.Equal("", tab.Status);
    }

    [Fact]
    public void View_logs_says_so_when_there_are_none()
    {
        NullPlatform platform = new();
        MonitorViewModel tab = Open(new ScriptedMonitor(), platform);

        tab.ViewLogsCommand.Execute(null);

        Assert.Empty(platform.OpenedFiles);
        Assert.NotEqual("", tab.Status);
    }

    [Fact]
    public void The_log_is_read_again_while_the_tab_is_shown_and_not_otherwise()
    {
        ScriptedMonitor monitor = new();
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true, Nickname = "Me" }) with { Monitor = monitor };
        MainViewModel main = new(services);
        int atStart = monitor.Reads;

        for (int i = 0; i < 8; i++)
            main.Poll();
        Assert.Equal(atStart, monitor.Reads);

        main.GoTo(TabId.Monitor);
        monitor.Lines.Add("late");
        for (int i = 0; i < 4; i++)
            main.Poll();

        Assert.Equal("late", main.Monitor.LogLines[^1]);
    }
}
