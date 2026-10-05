using System.Net;
using System.Net.Http.Headers;

namespace JoinFS.UI.Services;

/// <summary>
/// The tiles of openstreetmap.org, as its tile usage policy asks: a User-Agent that says who is asking, the attribution, a local cache
/// that is used for a week before a tile is asked for again, and no more than two requests at a time.
/// </summary>
public sealed class OsmTileSource : IMapTileSource, IDisposable
{
    private const string UrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly HttpClient _http;
    private readonly string? _cacheFolder;
    private readonly SemaphoreSlim _slots = new(2);

    /// <param name="userAgent">Says which app is asking, e.g. "JoinFS/26.6.0 (+https://github.com/tuduce/JoinFS)".</param>
    /// <param name="cacheFolder">Where the tiles are kept between runs, or null to keep none.</param>
    /// <param name="handler">Only for tests, to answer without a network.</param>
    public OsmTileSource(string userAgent, string? cacheFolder, HttpMessageHandler? handler = null)
    {
        _cacheFolder = cacheFolder;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("image/png"));
    }

    public string Attribution => "© OpenStreetMap contributors";
    public string AttributionUrl => "https://www.openstreetmap.org/copyright";
    public int MaxZoom => 19;

    public async Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken)
    {
        string? file = _cacheFolder is null ? null : Path.Combine(_cacheFolder, zoom.ToString(), x.ToString(), y + ".png");

        byte[]? cached = null;
        if (file is not null && File.Exists(file))
        {
            try
            {
                cached = await File.ReadAllBytesAsync(file, cancellationToken);
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MaxAge)
                    return cached;
            }
            catch (IOException)
            {
                cached = null;
            }
        }

        await _slots.WaitAsync(cancellationToken);
        try
        {
            using HttpResponseMessage response = await _http.GetAsync(string.Format(UrlTemplate, zoom, x, y), cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            Store(file, bytes);
            return bytes;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            // offline, or the server is not answering: an old tile is better than none
            return cached;
        }
        finally
        {
            _slots.Release();
        }
    }

    private static void Store(string? file, byte[] bytes)
    {
        if (file is null)
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllBytes(file, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // a cache that cannot be written is only a cache that does not help
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _slots.Dispose();
    }
}
