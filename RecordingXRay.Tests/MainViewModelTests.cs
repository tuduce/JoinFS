using RecordingXRay.ViewModels;

namespace RecordingXRay.Tests;

public sealed class MainViewModelTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-tests").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Starts_empty_and_ready()
    {
        MainViewModel viewModel = new();

        Assert.True(viewModel.IsEmpty);
        Assert.False(viewModel.HasRecording);
        Assert.Equal(StatusKind.Ready, viewModel.Status);
        Assert.Equal("Ready", viewModel.StatusText);
        Assert.Equal("No recording open", viewModel.FileName);
        Assert.All(viewModel.SummaryItems, item => Assert.True(item.IsPlaceholder));
    }

    [Fact]
    public async Task LoadAsync_publishes_the_recording_and_summary()
    {
        string path = RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 711.165);
        MainViewModel viewModel = new();

        await viewModel.LoadAsync(path);

        Assert.True(viewModel.HasRecording);
        Assert.False(viewModel.IsLoading);
        Assert.Equal(StatusKind.Loaded, viewModel.Status);
        Assert.Equal(path, viewModel.FilePath);
        Assert.Equal(Path.GetFileName(path), viewModel.FileName);
        Assert.EndsWith(Path.DirectorySeparatorChar.ToString(), viewModel.FileDirectory);
        Assert.Collection(
            viewModel.SummaryItems,
            item => Assert.Equal(("VERSION", "21005"), (item.Label, item.Value)),
            item => Assert.Equal(("AIRCRAFT", "1"), (item.Label, item.Value)),
            item => Assert.Equal(("OBJECTS", "0"), (item.Label, item.Value)),
            item => Assert.Equal(("FRAMES", "3"), (item.Label, item.Value)),
            item => Assert.Equal(("DURATION", "711.165 s", "11:51.165"), (item.Label, item.Value, item.Extra)));
        Assert.All(viewModel.SummaryItems, item => Assert.False(item.IsPlaceholder));
    }

    [Fact]
    public async Task LoadAsync_selects_the_first_aircraft_and_its_first_frame()
    {
        MainViewModel viewModel = new(_ => "name");

        await viewModel.LoadAsync(RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101));

        LaneViewModel lane = Assert.Single(viewModel.Lanes);
        Assert.Same(lane, viewModel.SelectedLane);
        Assert.Same(lane, viewModel.Browser.Lane);
        Assert.Equal(0, viewModel.Browser.SelectedRow!.Index);
        Assert.True(viewModel.Inspector.HasFrame);
        Assert.Equal("AircraftPosition", viewModel.Inspector.TypeName);
        Assert.Equal("YR-SCD · frame [0] · 0.026 s", viewModel.Inspector.Subtitle);
    }

    [Fact]
    public async Task Selecting_a_frame_in_the_browser_updates_the_inspector()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 0.2));

        viewModel.Browser.SelectedRow = viewModel.Browser.Rows[2];

        Assert.Equal("YR-SCD · frame [2] · 0.200 s", viewModel.Inspector.Subtitle);
    }

    [Fact]
    public async Task Loading_another_recording_replaces_the_lanes_and_resets_the_selection()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 0.2));
        viewModel.Browser.SelectedRow = viewModel.Browser.Rows[2];

        await viewModel.LoadAsync(RecordingFiles.WriteSingleAircraft(directory, 5));

        Assert.Equal(0, viewModel.Browser.SelectedRow!.Index);
        Assert.Equal("YR-SCD · frame [0] · 5.000 s", viewModel.Inspector.Subtitle);
        Assert.Single(viewModel.Browser.Rows);
    }

    [Fact]
    public async Task Copy_goes_through_the_window_supplied_clipboard()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WriteSingleAircraft(directory, 0.026));
        string? copied = null;
        viewModel.CopyText = text =>
        {
            copied = text;
            return Task.CompletedTask;
        };

        await viewModel.Inspector.CopyCommand.ExecuteAsync(null);

        Assert.StartsWith("Type: AircraftPosition", copied);
    }

    [Fact]
    public async Task LoadAsync_failure_reports_the_error_and_keeps_the_previous_recording()
    {
        string good = RecordingFiles.WriteSingleAircraft(directory, 1.0);
        string bad = Path.Combine(directory, "broken.jfs");
        await File.WriteAllBytesAsync(bad, [1, 0]); // version 1: unsupported
        MainViewModel viewModel = new();
        await viewModel.LoadAsync(good);

        await viewModel.LoadAsync(bad);

        Assert.True(viewModel.HasError);
        Assert.Contains("broken.jfs", viewModel.ErrorMessage);
        Assert.Equal(StatusKind.Error, viewModel.Status);
        Assert.False(viewModel.IsLoading);
        Assert.True(viewModel.HasRecording);
        Assert.Equal(good, viewModel.FilePath);
        Assert.Equal("1", viewModel.SummaryItems[3].Value);
    }

    [Fact]
    public async Task LoadAsync_on_a_missing_file_leaves_the_empty_state()
    {
        MainViewModel viewModel = new();

        await viewModel.LoadAsync(Path.Combine(directory, "missing.jfs"));

        Assert.True(viewModel.HasError);
        Assert.True(viewModel.IsEmpty);
        Assert.Equal(StatusKind.Error, viewModel.Status);
    }

    [Fact]
    public async Task A_later_successful_load_clears_the_error()
    {
        MainViewModel viewModel = new();
        await viewModel.LoadAsync(Path.Combine(directory, "missing.jfs"));

        await viewModel.LoadAsync(RecordingFiles.WriteSingleAircraft(directory, 1.0));

        Assert.False(viewModel.HasError);
        Assert.Equal(StatusKind.Loaded, viewModel.Status);
    }

    [Fact]
    public void DismissError_clears_the_message()
    {
        MainViewModel viewModel = new() { ErrorMessage = "boom" };

        viewModel.DismissErrorCommand.Execute(null);

        Assert.False(viewModel.HasError);
    }

    [Fact]
    public async Task Open_loads_the_picked_file_and_ignores_a_cancelled_pick()
    {
        string path = RecordingFiles.WriteSingleAircraft(directory, 1.0);
        MainViewModel viewModel = new() { PickFile = () => Task.FromResult<string?>(null) };

        await viewModel.OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.IsEmpty);

        viewModel.PickFile = () => Task.FromResult<string?>(path);
        await viewModel.OpenCommand.ExecuteAsync(null);
        Assert.True(viewModel.HasRecording);
    }
}
