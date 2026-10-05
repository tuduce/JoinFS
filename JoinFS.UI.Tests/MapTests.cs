using System.Net;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
using JoinFS.UI.ViewModels.Tabs;
using JoinFS.UI.Views.Controls;

namespace JoinFS.UI.Tests;

public class WebMercatorTests
{
    [Fact]
    public void The_world_is_one_tile_at_zoom_0_with_the_equator_and_the_prime_meridian_in_the_middle()
    {
        Assert.Equal(256, WebMercator.WorldSize(0));
        Assert.Equal(128, WebMercator.X(0, 0), 6);
        Assert.Equal(128, WebMercator.Y(0, 0), 6);
        Assert.Equal(0, WebMercator.X(-180, 0), 6);
        Assert.Equal(256, WebMercator.X(180, 0), 6);
    }

    [Theory]
    [InlineData(47.45, 8.55, 3)]
    [InlineData(-34.56, -58.42, 7.5)]
    [InlineData(35.55, 139.78, 12)]
    public void A_position_survives_the_trip_to_pixels_and_back(double latitude, double longitude, double zoom)
    {
        Assert.Equal(latitude, WebMercator.Latitude(WebMercator.Y(latitude, zoom), zoom), 6);
        Assert.Equal(longitude, WebMercator.Longitude(WebMercator.X(longitude, zoom), zoom), 6);
    }

    [Fact]
    public void Longitudes_are_brought_into_range()
    {
        Assert.Equal(-179, WebMercator.WrapLongitude(181), 9);
        Assert.Equal(179, WebMercator.WrapLongitude(-181), 9);
        Assert.Equal(10, WebMercator.WrapLongitude(370), 9);
    }

    [Fact]
    public void The_poles_are_cut_off_where_the_projection_ends()
    {
        Assert.Equal(WebMercator.Y(WebMercator.MaxLatitude, 2), WebMercator.Y(90, 2), 6);
    }
}

public class MapViewportTests
{
    private static MapViewport Viewport(double width = 800, double height = 400)
    {
        MapViewport viewport = new();
        viewport.Resize(width, height);
        return viewport;
    }

    [Fact]
    public void Fit_with_no_aircraft_shows_the_world()
    {
        MapViewport viewport = Viewport();

        viewport.Fit([]);

        Assert.InRange(viewport.Zoom, viewport.MinZoom, 2);
    }

    [Fact]
    public void Fit_with_one_aircraft_centres_on_it_at_a_close_zoom()
    {
        MapViewport viewport = Viewport();

        viewport.Fit([(47.45, 8.55)]);

        Assert.Equal(MapViewport.SingleAircraftZoom, viewport.Zoom);
        Assert.Equal(47.45, viewport.CenterLatitude, 4);
        Assert.Equal(8.55, viewport.CenterLongitude, 4);
    }

    [Fact]
    public void Fit_with_several_aircraft_shows_them_all_inside_the_padding()
    {
        MapViewport viewport = Viewport();
        (double, double)[] positions = [(47.45, 8.55), (46.2, 6.1), (51.47, -0.45), (40.47, -3.56)];

        viewport.Fit(positions, padding: 40);

        foreach (var (latitude, longitude) in positions)
        {
            Point at = viewport.ToScreen(latitude, longitude);
            Assert.InRange(at.X, 39, 761);
            Assert.InRange(at.Y, 39, 361);
        }
    }

    [Fact]
    public void Fit_zooms_in_as_far_as_the_aircraft_allow_and_no_further()
    {
        MapViewport viewport = Viewport();

        viewport.Fit([(47.0, 8.0), (47.0001, 8.0001)]);

        Assert.Equal(MapViewport.MaxFitZoom, viewport.Zoom);
    }

    [Fact]
    public void Dragging_moves_the_map_with_the_pointer()
    {
        MapViewport viewport = Viewport();
        viewport.SetView(47, 8, 6);
        var (latitude, longitude) = viewport.ToGeo(new Point(300, 150));

        viewport.PanBy(25, -10);

        Point at = viewport.ToScreen(latitude, longitude);
        Assert.Equal(325, at.X, 3);
        Assert.Equal(140, at.Y, 3);
    }

    [Fact]
    public void Zooming_keeps_the_place_under_the_pointer_where_it_is()
    {
        MapViewport viewport = Viewport();
        viewport.SetView(47, 8, 6);
        Point pointer = new(620, 90);
        var (latitude, longitude) = viewport.ToGeo(pointer);

        viewport.ZoomAt(pointer, 1.5);

        Assert.Equal(7.5, viewport.Zoom, 9);
        Point at = viewport.ToScreen(latitude, longitude);
        Assert.Equal(pointer.X, at.X, 3);
        Assert.Equal(pointer.Y, at.Y, 3);
    }

    [Fact]
    public void Zoom_stays_between_what_fills_the_view_and_what_the_tiles_have()
    {
        MapViewport viewport = Viewport();
        viewport.MaxZoom = 12;
        viewport.SetView(47, 8, 6);

        viewport.ZoomAt(new Point(400, 200), 100);
        Assert.Equal(12, viewport.Zoom);

        viewport.ZoomAt(new Point(400, 200), -100);
        Assert.Equal(viewport.MinZoom, viewport.Zoom, 9);
        // at that zoom the world is as high as the view
        Assert.Equal(viewport.Height, WebMercator.WorldSize(viewport.Zoom), 6);
    }

    [Fact]
    public void A_position_across_the_antimeridian_is_drawn_next_to_the_centre_not_a_world_away()
    {
        MapViewport viewport = Viewport();
        viewport.SetView(0, 179, 3);

        Point across = viewport.ToScreen(0, -179);

        // 2 degrees east of the centre
        Assert.Equal(400 + 2.0 / 360 * WebMercator.WorldSize(3), across.X, 3);
    }

    [Fact]
    public void Dragging_over_the_antimeridian_wraps_the_centre()
    {
        MapViewport viewport = Viewport();
        viewport.SetView(0, 179.5, 3);

        viewport.PanBy(-100, 0); // the map moves west, the centre goes east

        Assert.InRange(viewport.CenterLongitude, -180, 180);
        Assert.True(viewport.CenterLongitude < 0);
    }
}

public class HomeMapMarkerTests
{
    private static AircraftInfo Sample(string id) => SampleData.Aircraft[0] with { Id = id, Callsign = id };

    [Fact]
    public void Only_aircraft_with_a_position_are_on_the_map()
    {
        AircraftInfo[] list =
        [
            Sample("A") with { Latitude = 47.0, Longitude = 8.0 },
            Sample("B") with { Latitude = null, Longitude = null },
            Sample("C") with { Latitude = 0, Longitude = 0 }, // not placed yet
            Sample("D") with { Latitude = 0, Longitude = 12.5 }, // really on the equator
        ];

        List<MapMarker> markers = HomeViewModel.MarkersOf(list);

        Assert.Equal(["A", "D"], markers.Select(m => m.Id));
    }

    [Fact]
    public void An_aircraft_listed_twice_is_on_the_map_once()
    {
        AircraftInfo a = Sample("A") with { Latitude = 47.0, Longitude = 8.0 };

        Assert.Single(HomeViewModel.MarkersOf([a, a]));
    }

    [Fact]
    public void A_marker_carries_the_callsign_the_place_and_the_heading()
    {
        AircraftInfo a = Sample("A") with { Callsign = "HB-TDX", Latitude = 47.45, Longitude = 8.55, Heading = 90 };

        MapMarker marker = Assert.Single(HomeViewModel.MarkersOf([a]));

        Assert.Equal(new MapMarker("A", "HB-TDX", 47.45, 8.55, 90), marker);
    }

    [Fact]
    public void An_aircraft_without_a_heading_points_north()
    {
        AircraftInfo a = Sample("A") with { Latitude = 47.0, Longitude = 8.0, Heading = null };

        Assert.Equal(0, Assert.Single(HomeViewModel.MarkersOf([a])).Heading);
    }

    [Fact]
    public void Home_puts_the_aircraft_of_the_list_on_the_map_and_follows_the_list()
    {
        Rig rig = new();
        HomeViewModel home = rig.Main.Home;
        Assert.Equal(rig.Services.Traffic.GetAircraft().Count, home.MapMarkers.Count);
        Assert.True(home.HasMapMarkers);

        rig.Services.Traffic.IncludeHubAircraft = true;
        home.Refresh();

        Assert.Equal(rig.Services.Traffic.GetAircraft().Count, home.MapMarkers.Count);
        Assert.Contains(home.MapMarkers, m => m.Id == "HUB-001");
    }

    [Fact]
    public void The_attribution_opens_its_page()
    {
        Rig rig = new();
        rig.Main.Home.OpenMapAttributionCommand.Execute(null);
        Assert.Empty(rig.Platform.OpenedUrls); // the fake map has nothing to credit

        NullPlatform platform = new();
        AppServices services = FakeServices.Create(TimeSpan.Zero, new UserSettings { Onboarded = true }, platform) with { MapTiles = new CreditedTiles() };
        HomeViewModel home = new ViewModels.MainViewModel(services).Home;

        Assert.True(home.HasMapAttribution);
        home.OpenMapAttributionCommand.Execute(null);
        Assert.Equal(["https://example.org/credit"], platform.OpenedUrls);
    }

    private sealed class CreditedTiles : IMapTileSource
    {
        public string Attribution => "© Somebody";
        public string AttributionUrl => "https://example.org/credit";
        public int MaxZoom => 19;
        public Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }
}

public sealed class OsmTileSourceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "joinfs-tiles-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, true);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    [Fact]
    public async Task A_tile_is_asked_for_with_a_user_agent_that_says_who_is_asking()
    {
        Handler handler = new(_ => Ok([1, 2, 3]));
        using OsmTileSource source = new("JoinFS/26.6.0 (+https://github.com/tuduce/JoinFS)", null, handler);

        byte[]? tile = await source.GetTileAsync(5, 17, 11, CancellationToken.None);

        Assert.Equal([1, 2, 3], tile);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https://tile.openstreetmap.org/5/17/11.png", request.RequestUri!.ToString());
        Assert.Contains("JoinFS/26.6.0", request.Headers.UserAgent.ToString());
    }

    [Fact]
    public async Task A_tile_is_kept_and_not_asked_for_again_while_it_is_fresh()
    {
        Handler handler = new(_ => Ok([9, 9]));
        using OsmTileSource source = new("JoinFS/test", _folder, handler);

        await source.GetTileAsync(3, 1, 2, CancellationToken.None);
        byte[]? again = await source.GetTileAsync(3, 1, 2, CancellationToken.None);

        Assert.Equal([9, 9], again);
        Assert.Single(handler.Requests);
        Assert.True(File.Exists(Path.Combine(_folder, "3", "1", "2.png")));
    }

    [Fact]
    public async Task A_stale_tile_is_asked_for_again()
    {
        Handler handler = new(_ => Ok([2]));
        using OsmTileSource source = new("JoinFS/test", _folder, handler);
        string file = Path.Combine(_folder, "3", "1", "2.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, [1]);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8));

        byte[]? tile = await source.GetTileAsync(3, 1, 2, CancellationToken.None);

        Assert.Equal([2], tile);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task When_the_server_cannot_be_reached_an_old_tile_is_better_than_none()
    {
        Handler handler = new(_ => throw new HttpRequestException("offline"));
        using OsmTileSource source = new("JoinFS/test", _folder, handler);
        string file = Path.Combine(_folder, "3", "1", "2.png");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, [1]);
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-30));

        Assert.Equal([1], await source.GetTileAsync(3, 1, 2, CancellationToken.None));
        Assert.Null(await source.GetTileAsync(3, 9, 9, CancellationToken.None));
    }

    [Fact]
    public async Task A_tile_that_does_not_exist_is_none_and_is_not_kept()
    {
        Handler handler = new(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using OsmTileSource source = new("JoinFS/test", _folder, handler);

        Assert.Null(await source.GetTileAsync(3, 1, 2, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_folder, "3", "1", "2.png")));
    }

    [Fact]
    public void The_map_is_credited_to_openstreetmap()
    {
        using OsmTileSource source = new("JoinFS/test", null);

        Assert.Contains("OpenStreetMap", source.Attribution);
        Assert.Equal("https://www.openstreetmap.org/copyright", source.AttributionUrl);
    }
}

public class MapViewRenderTests
{
    /// <summary>Tiles that are plain squares, alternating two colours by position, so the picture shows where each one was put.</summary>
    private sealed class CheckerTiles : IMapTileSource
    {
        private readonly byte[] _light, _dark;
        public List<(int Z, int X, int Y)> Asked { get; } = [];

        public CheckerTiles()
        {
            _light = Png(Color.Parse("#F2E9D0"));
            _dark = Png(Color.Parse("#B7D2A8"));
        }

        public string Attribution => "";
        public string AttributionUrl => "";
        public int MaxZoom => 19;

        public Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken)
        {
            lock (Asked)
                Asked.Add((zoom, x, y));
            return Task.FromResult<byte[]?>((x + y) % 2 == 0 ? _light : _dark);
        }

        private static byte[] Png(Color color)
        {
            using RenderTargetBitmap bitmap = new(new PixelSize(256, 256));
            using (DrawingContext context = bitmap.CreateDrawingContext(true))
                context.FillRectangle(new SolidColorBrush(color), new Rect(0, 0, 256, 256));
            using MemoryStream stream = new();
            bitmap.Save(stream);
            return stream.ToArray();
        }
    }

    private static void Pump(int rounds = 40)
    {
        for (int i = 0; i < rounds; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Thread.Sleep(15);
        }
    }

    private static (Window Window, MapView Map) Open(IMapTileSource tiles, IReadOnlyList<MapMarker> markers)
    {
        MapView map = new() { TileSource = tiles, Markers = markers };
        Window window = new() { Width = 640, Height = 400, Content = map };
        window.Show();
        return (window, map);
    }

    [AvaloniaFact]
    public void The_map_draws_the_tiles_it_asked_for_and_a_marker_for_each_aircraft()
    {
        CheckerTiles tiles = new();
        (Window window, MapView map) = Open(tiles, [new("A", "HB-TDX", 47.45, 8.55, 90), new("B", "LSGG1", 46.2, 6.1, 0)]);

        Pump();

        Assert.NotEmpty(tiles.Asked);
        // the map was fitted to the two aircraft: both are in view
        foreach (MapMarker marker in map.Markers!)
        {
            Point at = map.Viewport.ToScreen(marker.Latitude, marker.Longitude);
            Assert.InRange(at.X, 0, 640);
            Assert.InRange(at.Y, 0, 400);
        }

        string? folder = Environment.GetEnvironmentVariable("JOINFS_UI_SCREENSHOTS");
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
            window.CaptureRenderedFrame()?.Save(Path.Combine(folder, "map-checker.png"));
        }

        // a tile was drawn: its colour is somewhere on the map
        using WriteableBitmap? frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        Assert.True(HasColour(frame!, Color.Parse("#F2E9D0")) || HasColour(frame!, Color.Parse("#B7D2A8")));
        window.Close();
    }

    [AvaloniaFact]
    public void Dragging_moves_the_map_and_stops_following_the_aircraft()
    {
        CheckerTiles tiles = new();
        (Window window, MapView map) = Open(tiles, [new("A", "HB-TDX", 47.45, 8.55, 90)]);
        Pump(5);
        double before = map.Viewport.CenterLongitude;

        window.MouseDown(new Point(300, 200), Avalonia.Input.MouseButton.Left);
        window.MouseMove(new Point(200, 200));
        window.MouseUp(new Point(200, 200), Avalonia.Input.MouseButton.Left);
        Pump(5);

        Assert.True(map.Viewport.CenterLongitude > before); // dragged to the left: the map's centre moved east

        // a new aircraft no longer moves the map ...
        double moved = map.Viewport.CenterLongitude;
        map.Markers = [new("A", "HB-TDX", 47.45, 8.55, 90), new("Z", "FAR", -33.9, 151.2, 0)];
        Pump(5);
        Assert.Equal(moved, map.Viewport.CenterLongitude);

        // ... until Fit asks for it
        map.FitToMarkers();
        Pump(5);
        Assert.NotEqual(moved, map.Viewport.CenterLongitude);
        window.Close();
    }

    [AvaloniaFact]
    public void An_aircraft_that_only_flew_does_not_move_the_map()
    {
        (Window window, MapView map) = Open(new CheckerTiles(), [new("A", "HB-TDX", 47.45, 8.55, 90), new("B", "LSGG1", 46.2, 6.1, 0)]);
        Pump(5);
        double zoom = map.Viewport.Zoom;
        double centre = map.Viewport.CenterLatitude;

        map.Markers = [new("A", "HB-TDX", 47.60, 8.80, 90), new("B", "LSGG1", 46.3, 6.4, 0)];
        Pump(5);

        Assert.Equal(zoom, map.Viewport.Zoom);
        Assert.Equal(centre, map.Viewport.CenterLatitude);
        window.Close();
    }

    [AvaloniaFact]
    public void The_wheel_zooms_in_and_keeps_the_page_from_scrolling()
    {
        (Window window, MapView map) = Open(new CheckerTiles(), [new("A", "HB-TDX", 47.45, 8.55, 90), new("B", "LSGG1", 46.2, 6.1, 0)]);
        Pump(5);
        double zoom = map.Viewport.Zoom;

        window.MouseWheel(new Point(320, 200), new Vector(0, 1));
        Pump(2);

        Assert.True(map.Viewport.Zoom > zoom);
        window.Close();
    }

    [AvaloniaFact]
    public void A_map_with_no_tiles_still_shows_its_aircraft_and_does_not_fail()
    {
        (Window window, MapView map) = Open(new NoMapTiles(), [new("A", "HB-TDX", 47.45, 8.55, 90)]);

        Pump(5);

        Assert.Equal(MapViewport.SingleAircraftZoom, map.Viewport.Zoom);
        window.Close();
    }

    private static bool HasColour(WriteableBitmap frame, Color colour)
    {
        using var buffer = frame.Lock();
        byte[] pixels = new byte[buffer.RowBytes * buffer.Size.Height];
        System.Runtime.InteropServices.Marshal.Copy(buffer.Address, pixels, 0, pixels.Length);
        for (int y = 0; y < buffer.Size.Height; y += 7)
        {
            for (int x = 0; x < buffer.Size.Width; x += 7)
            {
                int at = y * buffer.RowBytes + x * 4;
                // the frame is BGRA or RGBA, whichever the platform draws in
                bool bgr = Math.Abs(pixels[at] - colour.B) < 6 && Math.Abs(pixels[at + 1] - colour.G) < 6 && Math.Abs(pixels[at + 2] - colour.R) < 6;
                bool rgb = Math.Abs(pixels[at] - colour.R) < 6 && Math.Abs(pixels[at + 1] - colour.G) < 6 && Math.Abs(pixels[at + 2] - colour.B) < 6;
                if (bgr || rgb)
                    return true;
            }
        }
        return false;
    }
}
