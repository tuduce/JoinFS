using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using RecordingXRay.Services;
using RecordingXRay.ViewModels;
using RecordingXRay.Views;

namespace RecordingXRay.Tests;

/// <summary>
/// Renders the main window headlessly. The assertions are light; the point is the PNG: set
/// <c>XRAY_SCREENSHOT_DIR</c> to a folder to get <c>empty.png</c> and <c>loaded.png</c> there, and
/// <c>XRAY_SAMPLE</c> to a real .jfs file to render that instead of the generated one.
/// </summary>
public sealed class ScreenshotTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-shots").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [AvaloniaFact]
    public void Empty_window_shows_the_open_prompt()
    {
        MainWindow window = Show(new MainViewModel());

        Save(window, "empty.png");
        Assert.True(window.IsVisible);
    }

    [AvaloniaFact]
    public async Task Loaded_window_shows_the_summary()
    {
        string path = Environment.GetEnvironmentVariable("XRAY_SAMPLE") is { Length: > 0 } sample && File.Exists(sample)
            ? sample
            : RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 711.165);
        MainViewModel viewModel = new();
        MainWindow window = Show(viewModel);

        await viewModel.LoadAsync(path);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Save(window, "loaded.png");
        Assert.True(viewModel.HasRecording);
    }

    [AvaloniaFact]
    public async Task Inspector_modes_render()
    {
        string path = Environment.GetEnvironmentVariable("XRAY_SAMPLE") is { Length: > 0 } sample && File.Exists(sample)
            ? sample
            : RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 711.165);
        MainViewModel viewModel = new();
        MainWindow window = Show(viewModel);
        await viewModel.LoadAsync(path);

        // Frame [3] of the sample is an IntegerVariables frame; in the generated file it is a position frame.
        viewModel.Browser.SelectFrame(Math.Min(3, viewModel.SelectedLane!.Frames.Count - 1));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "inspector-frame3.png");

        viewModel.Inspector.IsRaw = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "inspector-raw.png");
        Assert.True(viewModel.Inspector.HasFrame);
    }

    [AvaloniaFact]
    public async Task Timeline_scrubbed_and_zoomed_renders()
    {
        string path = Environment.GetEnvironmentVariable("XRAY_SAMPLE") is { Length: > 0 } sample && File.Exists(sample)
            ? sample
            : RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 711.165);
        MainViewModel viewModel = new();
        MainWindow window = Show(viewModel);
        await viewModel.LoadAsync(path);

        viewModel.SelectAircraft(viewModel.Lanes[^1]);
        viewModel.MoveCursor(300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "timeline-scrubbed.png");

        viewModel.Timeline.ZoomBy(40, 300);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "timeline-zoomed.png");
        Assert.True(viewModel.Timeline.ViewSpan < viewModel.Duration);
    }

    [AvaloniaFact]
    public async Task Map_following_and_zoomed_renders()
    {
        string path = Environment.GetEnvironmentVariable("XRAY_SAMPLE") is { Length: > 0 } sample && File.Exists(sample)
            ? sample
            : RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 711.165);
        MainViewModel viewModel = new();
        MainWindow window = Show(viewModel);
        await viewModel.LoadAsync(path);

        Avalonia.Threading.Dispatcher.UIThread.RunJobs(); // lay out, so the map knows its size and fits
        viewModel.SelectAircraft(viewModel.Lanes[^1]);
        viewModel.MoveCursor(120);
        viewModel.Map.Follow = true;
        viewModel.Map.ZoomBy(12, 0, 0);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "map-follow-zoomed.png");
        Assert.True(viewModel.Map.Follow);
    }

    /// <summary>Needs the internet and XRAY_TILES=1: renders the map over real OpenStreetMap tiles.</summary>
    [AvaloniaFact]
    public async Task Map_with_openstreetmap_tiles_renders()
    {
        if (Environment.GetEnvironmentVariable("XRAY_TILES") != "1")
        {
            return;
        }

        string path = Environment.GetEnvironmentVariable("XRAY_SAMPLE") is { Length: > 0 } sample && File.Exists(sample)
            ? sample
            : RecordingFiles.WriteSingleAircraft(directory, 0.026, 0.101, 711.165);
        using OsmTileSource tiles = new();
        MainViewModel viewModel = new(tiles: tiles);
        MainWindow window = Show(viewModel);
        await viewModel.LoadAsync(path);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        viewModel.SelectAircraft(viewModel.Lanes[^1]);
        viewModel.MoveCursor(300);

        // Give the tiles a few seconds to arrive.
        for (int i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }

        Save(window, "map-tiles.png");
        Assert.True(viewModel.Map.HasBasemap);
    }

    [AvaloniaFact]
    public void Empty_window_with_recent_files_renders()
    {
        string first = RecordingFiles.WriteSingleAircraft(directory, 1);
        string second = Path.Combine(directory, "tigers 3rd.jfs");
        File.Copy(first, second);
        MemorySettingsStore store = new();
        store.Save(new AppSettings { RecentFiles = [second, first] });
        MainViewModel viewModel = new(settingsStore: store);
        MainWindow window = Show(viewModel);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "empty-recent.png");

        viewModel.IsDragOver = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Save(window, "empty-dragover.png");
        Assert.Equal(2, viewModel.RecentFiles.Count);
    }

    private static MainWindow Show(MainViewModel viewModel)
    {
        MainWindow window = new() { DataContext = viewModel, Width = 1440, Height = 900 };
        window.Show();
        return window;
    }

    private static void Save(MainWindow window, string fileName)
    {
        string? folder = Environment.GetEnvironmentVariable("XRAY_SCREENSHOT_DIR");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(folder, fileName));
    }
}
