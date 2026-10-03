using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using RecordingXRay.Services;
using RecordingXRay.ViewModels;
using RecordingXRay.Views;
using RecordingXRay.Views.Controls;

namespace RecordingXRay.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-settings").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Settings_round_trip_through_a_file()
    {
        FileSettingsStore store = new(Path.Combine(directory, "sub", "settings.json"));

        store.Save(new AppSettings { RecentFiles = ["C:\\a.jfs", "C:\\b.jfs"], ShowBasemap = false });
        AppSettings loaded = store.Load();

        Assert.Equal(["C:\\a.jfs", "C:\\b.jfs"], loaded.RecentFiles);
        Assert.False(loaded.ShowBasemap);
    }

    [Fact]
    public void A_missing_or_damaged_file_gives_the_defaults()
    {
        string path = Path.Combine(directory, "settings.json");
        FileSettingsStore store = new(path);

        Assert.True(store.Load().ShowBasemap);
        Assert.Empty(store.Load().RecentFiles);

        File.WriteAllText(path, "{ this is not json");
        AppSettings loaded = store.Load();
        Assert.True(loaded.ShowBasemap);
        Assert.Empty(loaded.RecentFiles);
    }

    [Fact]
    public void A_file_that_cannot_be_written_is_ignored()
    {
        string blocker = Path.Combine(directory, "blocker");
        File.WriteAllText(blocker, "a file where a folder is needed");
        FileSettingsStore store = new(Path.Combine(blocker, "settings.json"));

        store.Save(new AppSettings()); // must not throw

        Assert.Empty(store.Load().RecentFiles);
    }

    [Fact]
    public void The_memory_store_keeps_copies()
    {
        MemorySettingsStore store = new();
        AppSettings settings = new() { RecentFiles = ["x"] };

        store.Save(settings);
        settings.RecentFiles.Add("y");

        Assert.Equal(["x"], store.Load().RecentFiles);
    }
}

public sealed class RecentFilesTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-recent").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private string File_(string name = "") =>
        RecordingFiles.WriteSingleAircraft(directory, 1.0) is var path && name.Length > 0 ? Rename(path, name) : path;

    private string Rename(string path, string name)
    {
        string target = Path.Combine(directory, name);
        File.Move(path, target);
        return target;
    }

    [Fact]
    public async Task A_loaded_file_goes_to_the_top_of_the_recent_list_without_duplicates()
    {
        MemorySettingsStore store = new();
        MainViewModel viewModel = new(_ => "n", settingsStore: store);
        string a = File_("a.jfs"), b = File_("b.jfs");

        await viewModel.LoadAsync(a);
        await viewModel.LoadAsync(b);
        await viewModel.LoadAsync(a);

        Assert.Equal([a, b], viewModel.RecentFiles.Select(r => r.Path));
        Assert.Equal(["a.jfs", "b.jfs"], viewModel.RecentFiles.Select(r => r.FileName));
        Assert.Equal(directory, viewModel.RecentFiles[0].Directory);
        Assert.True(viewModel.HasRecent);
        Assert.Equal([a, b], store.Load().RecentFiles);
    }

    [Fact]
    public async Task The_list_keeps_five()
    {
        MainViewModel viewModel = new(_ => "n");
        string[] files = Enumerable.Range(0, 7).Select(i => File_($"{i}.jfs")).ToArray();

        foreach (string file in files)
        {
            await viewModel.LoadAsync(file);
        }

        Assert.Equal(MainViewModel.MaxRecentFiles, viewModel.RecentFiles.Count);
        Assert.Equal(files[6], viewModel.RecentFiles[0].Path);
    }

    [Fact]
    public async Task The_list_survives_a_restart_and_drops_files_that_have_gone()
    {
        MemorySettingsStore store = new();
        string a = File_("a.jfs"), b = File_("b.jfs");
        MainViewModel first = new(_ => "n", settingsStore: store);
        await first.LoadAsync(a);
        await first.LoadAsync(b);
        File.Delete(a);

        MainViewModel second = new(_ => "n", settingsStore: store);

        Assert.Equal([b], second.RecentFiles.Select(r => r.Path));
    }

    [Fact]
    public async Task A_recent_entry_opens_its_file()
    {
        MainViewModel viewModel = new(_ => "n");
        string a = File_("a.jfs");
        await viewModel.LoadAsync(a);
        MainViewModel fresh = new(_ => "n", settingsStore: Store(a));

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)fresh.RecentFiles[0].Open!).ExecuteAsync(null);

        Assert.Equal(a, fresh.FilePath);
        Assert.True(fresh.HasRecording);
    }

    private static MemorySettingsStore Store(params string[] recent)
    {
        MemorySettingsStore store = new();
        store.Save(new AppSettings { RecentFiles = [.. recent] });
        return store;
    }

    [Fact]
    public async Task A_recent_file_that_fails_because_it_vanished_is_forgotten_but_a_corrupt_one_is_kept()
    {
        string good = File_("good.jfs");
        string corrupt = Path.Combine(directory, "corrupt.jfs");
        File.WriteAllBytes(corrupt, [1, 0]);
        MainViewModel viewModel = new(_ => "n", settingsStore: Store(good, corrupt));
        Assert.Equal(2, viewModel.RecentFiles.Count);

        await viewModel.LoadAsync(corrupt);
        Assert.Equal(2, viewModel.RecentFiles.Count);
        Assert.True(viewModel.HasError);

        File.Delete(good);
        await viewModel.LoadAsync(good);
        Assert.Equal([corrupt], viewModel.RecentFiles.Select(r => r.Path));
    }

    [Fact]
    public async Task Clear_empties_the_list_and_the_stored_copy()
    {
        MemorySettingsStore store = new();
        MainViewModel viewModel = new(_ => "n", settingsStore: store);
        await viewModel.LoadAsync(File_("a.jfs"));

        viewModel.ClearRecentCommand.Execute(null);

        Assert.False(viewModel.HasRecent);
        Assert.Empty(store.Load().RecentFiles);
    }

    [Fact]
    public async Task The_window_title_names_the_open_file()
    {
        MainViewModel viewModel = new(_ => "n");
        Assert.Equal("RecordingXRay", viewModel.WindowTitle);

        await viewModel.LoadAsync(File_("tigers.jfs"));

        Assert.Equal("tigers.jfs - RecordingXRay", viewModel.WindowTitle);
    }

    [Fact]
    public async Task The_basemap_choice_is_remembered()
    {
        MemorySettingsStore store = new();
        FakeTileSource tiles = new();
        MainViewModel first = new(_ => "n", tiles, store);
        await first.LoadAsync(File_("a.jfs"));
        Assert.True(first.Map.HasBasemap);
        Assert.True(first.Map.ShowBasemap);

        first.Map.ShowBasemap = false;
        MainViewModel second = new(_ => "n", tiles, store);
        await second.LoadAsync(File_("b.jfs"));

        Assert.False(store.Load().ShowBasemap);
        Assert.False(second.Map.ShowBasemap);
    }

    [Fact]
    public async Task Without_a_tile_source_the_map_has_no_basemap_and_makes_no_requests()
    {
        MainViewModel viewModel = new(_ => "n");
        await viewModel.LoadAsync(File_("a.jfs"));

        Assert.False(viewModel.Map.HasBasemap);
        Assert.False(viewModel.Map.ShowAttribution);
        Assert.Equal(string.Empty, viewModel.Map.Attribution);
    }
}

public class MapBasemapViewModelTests
{
    [Fact]
    public void The_credit_shows_with_the_basemap_and_hides_when_it_is_off()
    {
        FakeTileSource tiles = new();
        using MapViewModel map = new([], tiles: tiles);

        Assert.True(map.ShowAttribution);
        Assert.Equal("© Test map", map.Attribution);
        Assert.Equal("https://example.test/credit", map.AttributionUrl);

        map.ShowBasemap = false;
        Assert.False(map.ShowAttribution);
    }

    [Fact]
    public void A_new_tile_redraws_the_map_and_a_disposed_map_stops_listening()
    {
        FakeTileSource tiles = new();
        MapViewModel map = new([], tiles: tiles);
        int redraws = 0;
        map.Invalidated += () => redraws++;

        tiles.RaiseChanged();
        Assert.Equal(1, redraws);

        map.Dispose();
        Assert.False(tiles.HasSubscribers);
    }

    [Fact]
    public void Switching_the_basemap_redraws()
    {
        MapViewModel map = new([], tiles: new FakeTileSource());
        int redraws = 0;
        map.Invalidated += () => redraws++;

        map.ShowBasemap = false;

        Assert.Equal(1, redraws);
    }
}

public sealed class TimelineWheelTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-wheel").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private async Task<(MainWindow Window, MainViewModel ViewModel, TimelineControl Lanes)> ShowAsync()
    {
        MainViewModel viewModel = new(_ => "n");
        MainWindow window = new() { DataContext = viewModel, Width = 1440, Height = 900 };
        window.Show();
        await viewModel.LoadAsync(RecordingFiles.WriteAircraft(directory, ("A", RecordingFiles.Times(0, 10, 61))));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        TimelineControl lanes = window.GetVisualDescendants().OfType<TimelineControl>().Single();
        Assert.True(lanes.Bounds.Width > 100);
        return (window, viewModel, lanes);
    }

    [AvaloniaFact]
    public async Task The_wheel_over_the_lanes_zooms_in_and_out_around_the_pointer()
    {
        (MainWindow window, MainViewModel viewModel, TimelineControl lanes) = await ShowAsync();
        TimelineViewModel timeline = viewModel.Timeline;
        Point point = lanes.TranslatePoint(new Point(lanes.Bounds.Width / 2, 40), window)!.Value;
        double timeUnderPointer = lanes.TimeAt(lanes.Bounds.Width / 2);
        double span = timeline.ViewSpan;

        window.MouseWheel(point, new Vector(0, 3));
        Assert.True(timeline.ViewSpan < span);
        Assert.Equal(timeUnderPointer, lanes.TimeAt(lanes.Bounds.Width / 2), precision: 3); // the point under the pointer stays put

        double zoomedIn = timeline.ViewSpan;
        window.MouseWheel(point, new Vector(0, -3));
        Assert.True(timeline.ViewSpan > zoomedIn);
    }

    [AvaloniaFact]
    public async Task Shift_and_the_wheel_pan_the_zoomed_timeline()
    {
        (MainWindow window, MainViewModel viewModel, TimelineControl lanes) = await ShowAsync();
        viewModel.Timeline.ZoomBy(8, 300);
        double start = viewModel.Timeline.ViewStart;
        Point point = lanes.TranslatePoint(new Point(lanes.Bounds.Width / 2, 40), window)!.Value;

        window.MouseWheel(point, new Vector(0, -2), RawInputModifiers.Shift);

        Assert.NotEqual(start, viewModel.Timeline.ViewStart);
    }

    [AvaloniaFact]
    public async Task The_wheel_over_the_aircraft_names_does_not_zoom()
    {
        (MainWindow window, MainViewModel viewModel, TimelineControl lanes) = await ShowAsync();
        double span = viewModel.Timeline.ViewSpan;
        Point onNames = lanes.TranslatePoint(new Point(-100, 40), window)!.Value;

        window.MouseWheel(onNames, new Vector(0, 3));

        Assert.Equal(span, viewModel.Timeline.ViewSpan);
    }
}
