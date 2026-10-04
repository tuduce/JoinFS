using System.Net;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using RecordingXRay.Services;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Tests;

public class TileMathTests
{
    [Theory]
    [InlineData(256, 0)]
    [InlineData(256 * 1024, 10)]
    [InlineData(256 * 1024 * 1.2, 10)]
    [InlineData(256 * 1024 * 1.5, 11)]
    [InlineData(1, 0)]
    [InlineData(1e12, 19)]
    public void Zoom_follows_the_scale_and_stays_in_range(double scale, int expected) =>
        Assert.Equal(expected, TileMath.ZoomFor(scale));

    [Fact]
    public void Visible_tiles_cover_the_view_with_the_middle_one_first()
    {
        double scale = 256 * 1024 * 1.1; // zoom 10, tiles about 282 px
        double centerX = MapProjection.WorldX(0.231), centerY = MapProjection.WorldY(0.967);

        IReadOnlyList<TilePlacement> tiles = TileMath.Visible(centerX, centerY, scale, 800, 500);

        Assert.All(tiles, tile => Assert.Equal(10, tile.Key.Zoom));
        Assert.All(tiles, tile => Assert.InRange(tile.Size, 280, 284));
        // Together they cover the whole view.
        Assert.True(tiles.Min(t => t.X) <= 0 && tiles.Max(t => t.X + t.Size) >= 800);
        Assert.True(tiles.Min(t => t.Y) <= 0 && tiles.Max(t => t.Y + t.Size) >= 500);
        // The tile under the middle of the view comes first.
        TilePlacement first = tiles[0];
        Assert.InRange(400, first.X, first.X + first.Size);
        Assert.InRange(250, first.Y, first.Y + first.Size);
        Assert.Equal(tiles.Count, tiles.Select(t => t.Key).Distinct().Count());
    }

    [Fact]
    public void Tile_columns_wrap_round_the_world_and_rows_stop_at_the_poles()
    {
        // Straddling the antimeridian at zoom 3 (8 columns), near the north pole.
        IReadOnlyList<TilePlacement> tiles = TileMath.Visible(0.001, 0.02, 256 * 8, 800, 800);

        Assert.All(tiles, tile => Assert.InRange(tile.Key.X, 0, 7));
        Assert.Contains(tiles, tile => tile.Key.X == 7); // west of the antimeridian
        Assert.Contains(tiles, tile => tile.Key.X == 0);
        Assert.All(tiles, tile => Assert.InRange(tile.Key.Y, 0, 7));
    }

    [Fact]
    public void An_empty_view_has_no_tiles() => Assert.Empty(TileMath.Visible(0.5, 0.5, 1000, 0, 0));

    [Fact]
    public void A_tile_knows_its_parent()
    {
        Assert.Equal(new TileKey(9, 5, 3), new TileKey(10, 11, 7).Parent);
        Assert.Equal(new TileKey(0, 0, 0), new TileKey(1, 1, 1).Parent);
    }

    [Fact]
    public void A_coarser_tile_supplies_the_matching_part_of_a_finer_one()
    {
        TileKey parent = new(5, 10, 6);
        Size size = new(256, 256);

        Assert.Equal(new Rect(0, 0, 128, 128), TileMath.SourceRect(new TileKey(6, 20, 12), parent, size));
        Assert.Equal(new Rect(128, 128, 128, 128), TileMath.SourceRect(new TileKey(6, 21, 13), parent, size));
        Assert.Equal(new Rect(64, 192, 64, 64), TileMath.SourceRect(new TileKey(7, 41, 27), parent, size)); // two levels down
        Assert.Equal(new Rect(0, 0, 256, 256), TileMath.SourceRect(parent, parent, size));
        Assert.Null(TileMath.SourceRect(new TileKey(6, 22, 12), parent, size)); // not inside it
        Assert.Null(TileMath.SourceRect(new TileKey(4, 5, 3), parent, size)); // coarser than it
    }
}

public sealed class OsmTileSourceTests : IDisposable
{
    private readonly string directory = Directory.CreateTempSubdirectory("xray-tiles").FullName;

    public void Dispose() => Directory.Delete(directory, recursive: true);

    private static byte[] Png()
    {
        using WriteableBitmap bitmap = new(new PixelSize(8, 8), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using MemoryStream stream = new();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private readonly List<HttpRequestMessage> requests = [];

        public IReadOnlyList<HttpRequestMessage> Requests
        {
            get
            {
                lock (requests)
                {
                    return requests.ToArray();
                }
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (requests)
            {
                requests.Add(request);
            }

            return Task.FromResult(respond(request));
        }
    }

    private static HttpResponseMessage Ok(byte[] bytes) => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };

    private static async Task WaitAsync(Func<bool> done)
    {
        for (int i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(50);
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaFact]
    public async Task A_tile_is_downloaded_once_with_an_identifying_user_agent_and_then_served_from_memory()
    {
        byte[] png = Png();
        FakeHandler handler = new(_ => Ok(png));
        using OsmTileSource source = new(directory, handler);
        int changed = 0;
        source.TilesChanged += () => changed++;
        TileKey key = new(10, 540, 305);

        Assert.Null(source.TryGet(key));
        source.Request(key);
        await WaitAsync(() => source.TryGet(key) is not null);

        Assert.NotNull(source.TryGet(key));
        Assert.Equal(1, changed);
        HttpRequestMessage request = Assert.Single(handler.Requests);
        Assert.Equal("https://tile.openstreetmap.org/10/540/305.png", request.RequestUri!.ToString());
        Assert.Contains("RecordingXRay", request.Headers.UserAgent.ToString());

        source.Request(key);
        await Task.Delay(100);
        Assert.Single(handler.Requests);
    }

    [AvaloniaFact]
    public async Task A_tile_on_disk_is_used_without_the_network()
    {
        byte[] png = Png();
        TileKey key = new(7, 70, 40);
        using (OsmTileSource first = new(directory, new FakeHandler(_ => Ok(png))))
        {
            first.Request(key);
            await WaitAsync(() => first.TryGet(key) is not null);
        }

        FakeHandler offline = new(_ => throw new HttpRequestException("offline"));
        using OsmTileSource second = new(directory, offline);
        second.Request(key);
        await WaitAsync(() => second.TryGet(key) is not null);

        Assert.NotNull(second.TryGet(key));
        Assert.Empty(offline.Requests);
    }

    [AvaloniaFact]
    public async Task A_failed_download_is_not_retried_straight_away_and_never_throws()
    {
        FakeHandler handler = new(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using OsmTileSource source = new(null, handler);
        TileKey key = new(3, 1, 1);

        source.Request(key);
        await WaitAsync(() => handler.Requests.Count > 0);
        await Task.Delay(150);
        source.Request(key);
        await Task.Delay(150);

        Assert.Null(source.TryGet(key));
        Assert.Single(handler.Requests);
    }

    [AvaloniaFact]
    public async Task Tiles_outside_the_world_are_not_requested()
    {
        FakeHandler handler = new(_ => Ok(Png()));
        using OsmTileSource source = new(null, handler);

        source.Request(new TileKey(2, 4, 0));
        source.Request(new TileKey(2, 0, -1));
        source.Request(new TileKey(25, 0, 0));
        await Task.Delay(200);

        Assert.Empty(handler.Requests);
    }

    [AvaloniaFact]
    public async Task A_tile_that_is_no_longer_wanted_is_skipped()
    {
        FakeHandler handler = new(_ => Ok(Png()));
        using OsmTileSource source = new(null, handler);
        TileKey wanted = new(4, 1, 1), notWanted = new(4, 9, 9);
        source.SetWanted([wanted]);

        source.Request(notWanted);
        source.Request(wanted);
        await WaitAsync(() => source.TryGet(wanted) is not null);
        await Task.Delay(100);

        Assert.NotNull(source.TryGet(wanted));
        Assert.Null(source.TryGet(notWanted));
        Assert.Single(handler.Requests);
    }

    [AvaloniaFact]
    public async Task The_memory_cache_drops_the_least_recently_used_tile()
    {
        byte[] png = Png();
        using OsmTileSource source = new(null, new FakeHandler(_ => Ok(png)), memoryCapacity: 2);
        TileKey a = new(5, 1, 1), b = new(5, 2, 1), c = new(5, 3, 1);

        foreach (TileKey key in new[] { a, b })
        {
            source.Request(key);
            await WaitAsync(() => source.TryGet(key) is not null);
        }

        Assert.NotNull(source.TryGet(a)); // a is now newer than b
        source.Request(c);
        await WaitAsync(() => source.TryGet(c) is not null);

        Assert.NotNull(source.TryGet(a));
        Assert.NotNull(source.TryGet(c));
        Assert.Null(source.TryGet(b));
    }

    [Fact]
    public void The_credit_names_openstreetmap()
    {
        using OsmTileSource source = new();

        Assert.Equal("© OpenStreetMap contributors", source.Attribution);
        Assert.Equal("https://www.openstreetmap.org/copyright", source.AttributionUrl);
    }
}

internal sealed class FakeTileSource : ITileSource
{
    public event Action? TilesChanged;

    public string Attribution => "© Test map";

    public string AttributionUrl => "https://example.test/credit";

    public List<TileKey> Requested { get; } = [];

    public IReadOnlyCollection<TileKey> Wanted { get; private set; } = [];

    public Bitmap? TryGet(TileKey key) => null;

    public void Request(TileKey key) => Requested.Add(key);

    public void SetWanted(IReadOnlyCollection<TileKey> wanted) => Wanted = wanted;

    public void RaiseChanged() => TilesChanged?.Invoke();

    public bool HasSubscribers => TilesChanged is not null;
}
