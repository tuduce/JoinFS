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

public class MapProjectionTests
{
    [Fact]
    public void World_coordinates_run_zero_to_one_and_round_trip()
    {
        Assert.Equal(0, MapProjection.WorldX(-Math.PI), precision: 12);
        Assert.Equal(1, MapProjection.WorldX(Math.PI), precision: 12);
        Assert.Equal(0.5, MapProjection.WorldX(0), precision: 12);
        Assert.Equal(0.5, MapProjection.WorldY(0), precision: 12);
        Assert.True(MapProjection.WorldY(1.0) < 0.5); // north is up
        Assert.Equal(1.0, MapProjection.LatitudeRadians(MapProjection.WorldY(1.0)), precision: 9);
        Assert.Equal(0.231041585664082, MapProjection.LongitudeRadians(MapProjection.WorldX(0.231041585664082)), precision: 12);
    }

    [Fact]
    public void The_poles_are_clamped_not_infinite()
    {
        Assert.True(double.IsFinite(MapProjection.WorldY(Math.PI / 2)));
        Assert.InRange(MapProjection.WorldY(Math.PI / 2), -1e-9, 0.01);
    }

    [Fact]
    public void One_pixel_shrinks_with_latitude()
    {
        Assert.Equal(MapProjection.EarthCircumferenceMeters / 256, MapProjection.MetersPerPixel(256, 0), precision: 3);
        Assert.Equal(MapProjection.EarthCircumferenceMeters / 256 / 2, MapProjection.MetersPerPixel(256, Math.PI / 3), precision: 3);
    }

    [Fact]
    public void Coordinates_use_hemisphere_letters()
    {
        Assert.Equal("55.4293°N 13.2377°E", MapProjection.Coordinates(0.9674238236590125, 0.231041585664082));
        Assert.Equal("33.8675°S 151.2093°W", MapProjection.Coordinates(-0.5911, -2.6391));
    }

    [Theory]
    [InlineData(10, 110, "1 km", 100)]
    [InlineData(0.5, 110, "50 m", 100)]
    [InlineData(1, 110, "100 m", 100)]
    [InlineData(25, 110, "2 km", 80)]
    public void ScaleBar_picks_the_longest_round_distance_that_fits(double metersPerPixel, double maxPixels, string label, double pixels)
    {
        (_, string text, double width) = MapProjection.ScaleBar(metersPerPixel, maxPixels);

        Assert.Equal(label, text);
        Assert.Equal(pixels, width, precision: 6);
    }

    [Theory]
    [InlineData(0.07, 0.1)]
    [InlineData(0.04, 0.05)]
    [InlineData(0.1, 0.1)]
    [InlineData(1.5, 2)]
    [InlineData(0.0000001, 0.000001)]
    [InlineData(500, 50)]
    public void Graticule_steps_are_one_two_or_five_times_a_power_of_ten(double target, double expected) =>
        Assert.Equal(expected, MapProjection.NiceAngleStep(target), precision: 9);

    [Theory]
    [InlineData(13.2, 0.1, "E", "W", "13.2°E")]
    [InlineData(-55.45, 0.05, "N", "S", "55.45°S")]
    [InlineData(13, 1, "E", "W", "13°E")]
    [InlineData(13.123, 0.001, "E", "W", "13.123°E")]
    public void Graticule_labels_have_as_many_decimals_as_the_step(double degrees, double step, string positive, string negative, string expected) =>
        Assert.Equal(expected, MapProjection.AngleLabel(degrees, step, positive, negative));
}

public class MapTrackTests
{
    private static AircraftPositionFrame At(double time, double latitude, double longitude, float heading = 0) => new()
    {
        Type = FrameType.AircraftPosition,
        Time = time,
        Latitude = latitude,
        Longitude = longitude,
        Heading = heading,
    };

    private static LaneViewModel Lane(params RecordedFrame[] frames) => new(TestFrames.Aircraft("A", frames));

    [Fact]
    public void A_lane_without_position_frames_has_no_track()
    {
        Assert.Null(MapTrack.Create(Lane(TestFrames.Integers(1, (1, 1)), TestFrames.Event(2))));
        Assert.Null(MapTrack.Create(new LaneViewModel(new RecordedObject())));
    }

    [Fact]
    public void Frames_without_a_fix_or_with_bad_coordinates_are_skipped()
    {
        MapTrack? track = MapTrack.Create(Lane(
            At(0, 0, 0),
            At(1, double.NaN, 0.2),
            At(2, 0.9, 5),
            At(3, 0.9, 0.2),
            At(4, 0.9, 0.201)));

        Assert.Equal(2, track!.PointCount);
    }

    [Fact]
    public void Object_position_frames_count_too()
    {
        RecordedObject obj = new() { Model = "Balloon" };
        obj.Frames.Add(new ObjectPositionFrame { Type = FrameType.ObjectPosition, Time = 1, Latitude = 0.5, Longitude = 0.1 });

        Assert.NotNull(MapTrack.Create(new LaneViewModel(obj)));
    }

    [Fact]
    public void SampleAt_interpolates_between_frames_and_holds_the_ends()
    {
        MapTrack track = MapTrack.Create(Lane(At(10, 0.9, 0.2, 1f), At(20, 1.0, 0.4, 2f)))!;

        MapSample middle = track.SampleAt(15);
        Assert.Equal(0.95, middle.Latitude, precision: 9);
        Assert.Equal(0.3, middle.Longitude, precision: 9);
        Assert.Equal(1.5, middle.Heading, precision: 6);
        Assert.Equal(MapProjection.WorldX(0.3), middle.X, precision: 9);

        Assert.Equal(0.9, track.SampleAt(0).Latitude);
        Assert.Equal(1.0, track.SampleAt(99).Latitude);
        Assert.Equal(1.0, track.SampleAt(20).Latitude);
    }

    [Fact]
    public void Headings_interpolate_the_short_way_round()
    {
        MapTrack track = MapTrack.Create(Lane(At(0, 0.9, 0.2, 6.0f), At(10, 0.9, 0.3, 0.2f)))!;

        Assert.Equal(6.0 + (0.2 - 6.0 + 2 * Math.PI) * 0.5, track.SampleAt(5).Heading, precision: 5);
    }

    [Fact]
    public void The_trail_is_thinned_but_keeps_both_ends_and_the_exact_positions_stay_available()
    {
        // 1000 points, 0.06 m apart.
        RecordedFrame[] frames = Enumerable.Range(0, 1000).Select(i => (RecordedFrame)At(i, 0.9 + i * 1e-8, 0.2)).ToArray();
        MapTrack track = MapTrack.Create(Lane(frames))!;

        Assert.Equal(1000, track.PointCount);
        Assert.InRange(track.TrailCount, 10, 60);

        (var past, var future) = track.Split(0);
        Assert.Single(past); // only the start, and the cursor point is the start itself
        Assert.Equal(track.TrailCount, future.Count);
        Assert.Equal(MapProjection.WorldY(0.9 + 999 * 1e-8), future[^1].Y, precision: 12);
    }

    [Fact]
    public void Split_divides_the_trail_at_the_cursor_and_joins_the_halves()
    {
        RecordedFrame[] frames = Enumerable.Range(0, 11).Select(i => (RecordedFrame)At(i * 10, 0.9, 0.2 + i * 0.001)).ToArray();
        MapTrack track = MapTrack.Create(Lane(frames))!;

        (var past, var future) = track.Split(45);

        MapSample now = track.SampleAt(45);
        Assert.Equal((now.X, now.Y), past[^1]);
        Assert.Equal((now.X, now.Y), future[0]);
        Assert.Equal(MapProjection.WorldX(0.2), past[0].X, precision: 12);
        Assert.Equal(MapProjection.WorldX(0.21), future[^1].X, precision: 12);
        Assert.Equal(5 + 1, past.Count); // points at 0..40 plus the cursor point
    }

    [Fact]
    public void Split_outside_the_path_is_all_future_or_all_past_without_a_join_point()
    {
        MapTrack track = MapTrack.Create(Lane(At(10, 0.9, 0.2), At(20, 0.9, 0.3)))!;

        (var past, var future) = track.Split(0);
        Assert.Empty(past);
        Assert.Equal(2, future.Count);

        (past, future) = track.Split(99);
        Assert.Equal(2, past.Count);
        Assert.Empty(future);
    }
}

public sealed class MapViewModelTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-map").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static (double Time, double Lat, double Lon, float Heading)[] Flight(double start, double end, double lat, double lonFrom, double lonTo, float heading = 1.5f) =>
        Enumerable.Range(0, 11).Select(i => (start + (end - start) * i / 10, lat, lonFrom + (lonTo - lonFrom) * i / 10, heading)).ToArray();

    // A flies east 0..100 s; B 40..60 s a little to the north; C has no position data of interest at all (one frame).
    private async Task<MainViewModel> LoadAsync()
    {
        MainViewModel viewModel = new(_ => "name");
        await viewModel.LoadAsync(RecordingFiles.WritePositions(
            directory,
            ("A", Flight(0, 100, 0.9674, 0.2310, 0.2330)),
            ("B", Flight(40, 60, 0.9684, 0.2315, 0.2325, 3.0f))));
        viewModel.Map.SetViewport(800, 500);
        return viewModel;
    }

    private static LaneViewModel Lane(MainViewModel viewModel, string name) => viewModel.Lanes.Single(lane => lane.Name == name);

    [Fact]
    public async Task The_map_has_a_track_per_lane_with_positions_and_fits_them_all_on_the_first_viewport()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;

        Assert.Equal(2, map.Tracks.Count);
        Assert.True(map.HasTracks);
        foreach (MapTrack track in map.Tracks)
        {
            Assert.InRange(map.ToScreenX(track.MinX), 0, 800);
            Assert.InRange(map.ToScreenX(track.MaxX), 0, 800);
            Assert.InRange(map.ToScreenY(track.MinY), 0, 500);
            Assert.InRange(map.ToScreenY(track.MaxY), 0, 500);
        }

        // Not zoomed out needlessly: the flights fill most of the width.
        MapTrack a = map.Tracks[0];
        Assert.True(map.ToScreenX(a.MaxX) - map.ToScreenX(a.MinX) > 300);
    }

    [Fact]
    public async Task Markers_are_the_aircraft_with_data_at_the_cursor_plus_the_selected_one_last()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;

        // Cursor 0: only A has data (B starts at 40 s).
        Assert.Equal(["A"], map.Markers.Select(m => m.Lane.Name));
        Assert.Equal("1 of 2 aircraft", map.CountText);

        viewModel.MoveCursor(50);
        Assert.Equal(["B", "A"], map.Markers.Select(m => m.Lane.Name)); // the selected one (A) is drawn last
        Assert.Equal([false, true], map.Markers.Select(m => m.IsSelected));
        Assert.Equal("2 aircraft", map.CountText);
        Assert.Equal("0:50.000", map.TimeText);

        viewModel.SelectAircraft(Lane(viewModel, "B"));
        viewModel.MoveCursor(90);
        MapMarker selected = map.Markers[^1];
        Assert.Equal("B", selected.Lane.Name);
        Assert.False(selected.HasData); // dimmed, held at the end of its path
        Assert.Equal(MapProjection.WorldX(0.2325), selected.Sample.X, precision: 9);
        Assert.Equal(["A", "B"], map.Markers.Select(m => m.Lane.Name));
        Assert.Contains("· no data", map.ReadoutCoordinates);
        Assert.Equal("B", map.ReadoutName);
    }

    [Fact]
    public async Task Markers_sit_at_the_interpolated_position_and_the_readout_shows_it()
    {
        MainViewModel viewModel = await LoadAsync();

        viewModel.MoveCursor(50);

        MapMarker a = viewModel.Map.Markers.Single(m => m.Lane.Name == "A");
        Assert.Equal(0.2320, a.Sample.Longitude, precision: 9);
        Assert.Equal(1.5, a.Sample.Heading, precision: 6);
        Assert.Equal("55.4279°N 13.2926°E", viewModel.Map.ReadoutCoordinates);
    }

    [Fact]
    public async Task Selecting_an_aircraft_without_follow_does_not_move_the_view()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        viewModel.MoveCursor(50);
        (double x, double y, double scale) = (map.CenterX, map.CenterY, map.Scale);

        viewModel.SelectAircraft(Lane(viewModel, "B"));
        viewModel.MoveCursor(55);

        Assert.Equal((x, y, scale), (map.CenterX, map.CenterY, map.Scale));
    }

    [Fact]
    public async Task Follow_centres_the_selected_aircraft_and_keeps_it_there_as_the_cursor_moves()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        viewModel.MoveCursor(20);

        map.Follow = true;

        MapMarker selected = map.Markers[^1];
        Assert.Equal(selected.Sample.X, map.CenterX, precision: 12);
        Assert.Equal(selected.Sample.Y, map.CenterY, precision: 12);

        viewModel.MoveCursor(70);
        Assert.Equal(map.Markers[^1].Sample.X, map.CenterX, precision: 12);
        Assert.Equal(400, map.ToScreenX(map.Markers[^1].Sample.X), precision: 6);

        viewModel.SelectAircraft(Lane(viewModel, "B"));
        Assert.Equal(map.Markers[^1].Sample.X, map.CenterX, precision: 12);
        Assert.Equal("B", map.Markers[^1].Lane.Name);
    }

    [Fact]
    public async Task Dragging_the_map_pans_it_and_switches_follow_off_without_a_jump()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        viewModel.MoveCursor(20);
        map.Follow = true;
        (double x, double y) = (map.CenterX, map.CenterY);

        map.PanBy(30, -10);

        Assert.False(map.Follow);
        Assert.Equal(x - 30 / map.Scale, map.CenterX, precision: 12);
        Assert.Equal(y + 10 / map.Scale, map.CenterY, precision: 12);

        // Moving the cursor no longer drags the view along.
        double panned = map.CenterX;
        viewModel.MoveCursor(80);
        Assert.Equal(panned, map.CenterX);
    }

    [Fact]
    public async Task Turning_follow_on_recentres_and_turning_it_off_keeps_the_view()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        viewModel.MoveCursor(20);
        map.PanBy(100, 100);
        double[] panned = [map.CenterX, map.CenterY];

        map.Follow = true;
        Assert.Equal(map.Markers[^1].Sample.X, map.CenterX, precision: 12);

        double[] centred = [map.CenterX, map.CenterY];
        map.Follow = false;
        Assert.Equal(centred, new[] { map.CenterX, map.CenterY });
        Assert.NotEqual(panned, centred);
    }

    [Fact]
    public async Task Fit_all_shows_every_trail_again_and_turns_follow_off()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        (double x, double y, double scale) = (map.CenterX, map.CenterY, map.Scale);
        viewModel.MoveCursor(20);
        map.Follow = true;
        map.ZoomBy(8, 400, 250);

        map.FitAllCommand.Execute(null);

        Assert.False(map.Follow);
        Assert.Equal((x, y, scale), (map.CenterX, map.CenterY, map.Scale), new TupleComparer());
    }

    [Fact]
    public async Task Zoom_keeps_the_point_under_the_pointer_in_place_and_stays_in_range()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        double worldX = (200 - 400) / map.Scale + map.CenterX;

        map.ZoomBy(2, 200, 100);

        Assert.Equal(200, map.ToScreenX(worldX), precision: 6);

        map.ZoomBy(1e12, 400, 250);
        Assert.Equal(MapProjection.MaxScale, map.Scale);
        map.ZoomBy(1e-12, 400, 250);
        Assert.Equal(MapProjection.MinScale, map.Scale);
    }

    [Fact]
    public async Task Zoom_while_following_stays_centred_on_the_aircraft()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        viewModel.MoveCursor(20);
        map.Follow = true;

        map.ZoomBy(2, 10, 10);

        Assert.Equal(map.Markers[^1].Sample.X, map.CenterX, precision: 12);
    }

    [Fact]
    public async Task Picking_a_marker_selects_the_aircraft_with_the_usual_cursor_rule()
    {
        MainViewModel viewModel = await LoadAsync();
        viewModel.MoveCursor(10);

        viewModel.Map.Pick(Lane(viewModel, "B"));

        Assert.Equal("B", viewModel.SelectedLane!.Name);
        Assert.Equal(40, viewModel.Cursor); // B starts at 40 s
        Assert.True(viewModel.Map.Markers[^1].HasData);
    }

    [Fact]
    public async Task The_graticule_covers_the_view_with_labelled_lines_at_a_readable_spacing()
    {
        MainViewModel viewModel = await LoadAsync();

        (var longitudes, var latitudes) = viewModel.Map.Graticule();

        Assert.NotEmpty(longitudes);
        Assert.NotEmpty(latitudes);
        Assert.All(longitudes, line => Assert.InRange(line.Pixel, 0, 800));
        Assert.All(latitudes, line => Assert.InRange(line.Pixel, 0, 500));
        Assert.All(longitudes, line => Assert.Matches(@"^\d+(\.\d+)?°[EW]$", line.Label));
        Assert.All(latitudes, line => Assert.Matches(@"^\d+(\.\d+)?°[NS]$", line.Label));
        double spacing = longitudes[1].Pixel - longitudes[0].Pixel;
        Assert.InRange(spacing, 80, 400);
    }

    [Fact]
    public async Task The_scale_bar_follows_the_zoom()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel map = viewModel.Map;
        string before = map.ScaleBarText;

        map.ZoomBy(1000, 400, 250);

        Assert.NotEqual(before, map.ScaleBarText);
        Assert.InRange(map.ScaleBarWidth, 20, 110);
    }

    [Fact]
    public void An_empty_map_has_no_tracks_and_ignores_input()
    {
        MapViewModel map = new([]);
        map.SetViewport(800, 500);

        map.PanBy(10, 10);
        map.ZoomBy(2, 100, 100);
        map.FitAllCommand.Execute(null);
        map.Update(null, 0);

        Assert.False(map.HasTracks);
        Assert.Empty(map.Markers);
        Assert.False(map.HasReadout);
    }

    [Fact]
    public async Task Loading_another_recording_makes_a_new_map_that_fits_again()
    {
        MainViewModel viewModel = await LoadAsync();
        MapViewModel first = viewModel.Map;

        await viewModel.LoadAsync(RecordingFiles.WritePositions(directory, ("Z", Flight(0, 10, 0.5, 0.1, 0.12))));

        Assert.NotSame(first, viewModel.Map);
        Assert.Single(viewModel.Map.Tracks);
        Assert.Equal(["Z"], viewModel.Map.Markers.Select(m => m.Lane.Name));
    }

    private sealed class TupleComparer : IEqualityComparer<(double, double, double)>
    {
        public bool Equals((double, double, double) a, (double, double, double) b) =>
            Math.Abs(a.Item1 - b.Item1) < 1e-9 && Math.Abs(a.Item2 - b.Item2) < 1e-9 && Math.Abs(a.Item3 - b.Item3) < 1e-6;

        public int GetHashCode((double, double, double) value) => 0;
    }
}

public sealed class MapInteractionTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-mapui").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private async Task<(MainWindow Window, MainViewModel ViewModel, MapControl Control)> ShowAsync()
    {
        MainViewModel viewModel = new(_ => "name");
        MainWindow window = new() { DataContext = viewModel, Width = 1440, Height = 900 };
        window.Show();
        await viewModel.LoadAsync(RecordingFiles.WritePositions(
            directory,
            ("A", Enumerable.Range(0, 11).Select(i => ((double)i * 10, 0.9674, 0.2310 + i * 0.0002, 1.5f)).ToArray()),
            ("B", Enumerable.Range(0, 11).Select(i => ((double)i * 10, 0.9684, 0.2310 + i * 0.0002, 1.5f)).ToArray())));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        MapControl control = window.GetVisualDescendants().OfType<MapControl>().Single();
        Assert.True(control.Bounds.Width > 100, "the map should have been laid out");
        return (window, viewModel, control);
    }

    private static Point InWindow(MapControl control, MainWindow window, MapViewModel map, MapMarker marker) =>
        control.TranslatePoint(new Point(map.ToScreenX(marker.Sample.X), map.ToScreenY(marker.Sample.Y)), window)!.Value;

    [AvaloniaFact]
    public async Task Clicking_a_marker_selects_that_aircraft()
    {
        (MainWindow window, MainViewModel viewModel, MapControl control) = await ShowAsync();
        Assert.Equal("A", viewModel.SelectedLane!.Name);
        MapMarker b = viewModel.Map.Markers.Single(m => m.Lane.Name == "B");
        Point point = InWindow(control, window, viewModel.Map, b);

        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);

        Assert.Equal("B", viewModel.SelectedLane!.Name);
    }

    [AvaloniaFact]
    public async Task Dragging_from_a_marker_pans_the_map_and_does_not_select()
    {
        (MainWindow window, MainViewModel viewModel, MapControl control) = await ShowAsync();
        MapViewModel map = viewModel.Map;
        MapMarker b = map.Markers.Single(m => m.Lane.Name == "B");
        Point start = InWindow(control, window, map, b);
        double centerX = map.CenterX;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(40, 25), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(40, 25), MouseButton.Left);

        Assert.True(map.CenterX < centerX); // dragged right: the view moved left
        Assert.Equal("A", viewModel.SelectedLane!.Name); // the drag started on B's marker but was a pan, not a click
    }

    [AvaloniaFact]
    public async Task Dragging_the_map_while_following_turns_follow_off()
    {
        (MainWindow window, MainViewModel viewModel, MapControl control) = await ShowAsync();
        MapViewModel map = viewModel.Map;
        map.Follow = true;
        Point start = control.TranslatePoint(new Point(control.Bounds.Width / 2 + 120, control.Bounds.Height / 2 + 80), window)!.Value;
        double centerX = map.CenterX;

        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Vector(30, 10), RawInputModifiers.LeftMouseButton);
        window.MouseUp(start + new Vector(30, 10), MouseButton.Left);

        Assert.False(map.Follow);
        Assert.NotEqual(centerX, map.CenterX);
    }

    [AvaloniaFact]
    public async Task The_wheel_zooms_the_map()
    {
        (MainWindow window, MainViewModel viewModel, MapControl control) = await ShowAsync();
        double scale = viewModel.Map.Scale;
        Point center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;

        window.MouseWheel(center, new Vector(0, 2));

        Assert.True(viewModel.Map.Scale > scale);
    }
}
