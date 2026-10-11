using System.Net;
using System.Net.Http.Headers;

namespace JoinFS.UI.Services;

/// <summary>
/// Slippy-map tiles from a web server, as the tile usage policies of the free servers ask: a User-Agent that says who is asking, the
/// attribution, a local cache that is used for a week before a tile is asked for again, and no more than two requests at a time.
/// What differs between the maps (address, picture format, credit, highest zoom) is given by the subclass.
/// </summary>
public abstract class HttpTileSource : IMapTileSource, IDisposable
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    private readonly string _urlTemplate;
    private readonly string _extension;
    private readonly HttpClient _http;
    private readonly string? _cacheFolder;
    private readonly SemaphoreSlim _slots = new(2);

    /// <param name="urlTemplate">The address of a tile, with <c>{z}</c>, <c>{x}</c> and <c>{y}</c> in it.</param>
    /// <param name="extension">What the pictures are, without the dot ("png"); also the cache files' extension.</param>
    /// <param name="mediaType">What the server is asked for ("image/png").</param>
    /// <param name="userAgent">Says which app is asking, e.g. "JoinFS/26.6.0 (+https://github.com/tuduce/JoinFS)".</param>
    /// <param name="cacheFolder">Where the tiles are kept between runs, or null to keep none. Each map needs a folder of its own.</param>
    /// <param name="handler">Only for tests, to answer without a network.</param>
    protected HttpTileSource(string urlTemplate, string extension, string mediaType, string userAgent, string? cacheFolder, HttpMessageHandler? handler)
    {
        _urlTemplate = urlTemplate;
        _extension = extension;
        _cacheFolder = cacheFolder;
        _http = new HttpClient(handler ?? new HttpClientHandler()) { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(mediaType));
    }

    public abstract string Attribution { get; }
    public abstract string AttributionUrl { get; }
    public abstract int MaxZoom { get; }

    public async Task<byte[]?> GetTileAsync(int zoom, int x, int y, CancellationToken cancellationToken)
    {
        string? file = _cacheFolder is null ? null : Path.Combine(_cacheFolder, zoom.ToString(), x.ToString(), y + "." + _extension);

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
            string url = _urlTemplate.Replace("{z}", zoom.ToString()).Replace("{x}", x.ToString()).Replace("{y}", y.ToString());
            using HttpResponseMessage response = await _http.GetAsync(url, cancellationToken);
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
