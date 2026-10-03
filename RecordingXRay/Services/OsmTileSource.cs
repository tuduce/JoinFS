using Avalonia.Media.Imaging;

namespace RecordingXRay.Services;

/// <summary>
/// OpenStreetMap standard tiles, following the tile usage policy (https://operations.osmfoundation.org/policies/tiles/):
/// an identifying User-Agent, at most two downloads at a time, only the tiles in view, tiles kept on disk and in
/// memory so they are not asked for again, and no retry storm after a failure.
/// </summary>
public sealed class OsmTileSource : ITileSource, IDisposable
{
    public const string DefaultUrlTemplate = "https://tile.openstreetmap.org/{z}/{x}/{y}.png";

    private const int MaxParallelDownloads = 2;
    private static readonly TimeSpan DiskTileLifetime = TimeSpan.FromDays(14);
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromSeconds(30);

    private readonly HttpClient http;
    private readonly string urlTemplate;
    private readonly string? cacheDirectory;
    private readonly int memoryCapacity;
    private readonly SemaphoreSlim downloads = new(MaxParallelDownloads);
    private readonly SynchronizationContext? context = SynchronizationContext.Current;
    private readonly object gate = new();

    private readonly Dictionary<TileKey, LinkedListNode<(TileKey Key, Bitmap Bitmap)>> memory = [];
    private readonly LinkedList<(TileKey Key, Bitmap Bitmap)> recent = new();
    private readonly HashSet<TileKey> loading = [];
    private readonly Dictionary<TileKey, DateTime> failed = [];
    private HashSet<TileKey> wanted = [];

    /// <param name="cacheDirectory">Folder for downloaded tiles (z/x/y.png), or null to keep tiles in memory only.</param>
    /// <param name="handler">For tests; the default is a normal HTTP handler.</param>
    public OsmTileSource(string? cacheDirectory = null, HttpMessageHandler? handler = null, string urlTemplate = DefaultUrlTemplate, int memoryCapacity = 400)
    {
        this.cacheDirectory = cacheDirectory;
        this.urlTemplate = urlTemplate;
        this.memoryCapacity = memoryCapacity;
        http = handler is null ? new HttpClient() : new HttpClient(handler);
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RecordingXRay/1.0 (JoinFS recording viewer; https://github.com/tuduce/JoinFS)");
    }

    /// <summary>The usual place for the tile cache: %LOCALAPPDATA%\RecordingXRay\tiles.</summary>
    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RecordingXRay", "tiles");

    public event Action? TilesChanged;

    public string Attribution => "© OpenStreetMap contributors";

    public string AttributionUrl => "https://www.openstreetmap.org/copyright";

    public Bitmap? TryGet(TileKey key)
    {
        lock (gate)
        {
            if (!memory.TryGetValue(key, out LinkedListNode<(TileKey Key, Bitmap Bitmap)>? node))
            {
                return null;
            }

            recent.Remove(node);
            recent.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    public void SetWanted(IReadOnlyCollection<TileKey> keys)
    {
        lock (gate)
        {
            wanted = [.. keys];
        }
    }

    public void Request(TileKey key)
    {
        if (!IsValid(key))
        {
            return;
        }

        lock (gate)
        {
            if (memory.ContainsKey(key) || loading.Contains(key)
                || (failed.TryGetValue(key, out DateTime when) && DateTime.UtcNow - when < RetryAfterFailure))
            {
                return;
            }

            loading.Add(key);
        }

        _ = Task.Run(() => LoadAsync(key));
    }

    public void Dispose()
    {
        http.Dispose();
        downloads.Dispose();
    }

    private static bool IsValid(TileKey key) =>
        key.Zoom is >= 0 and <= TileMath.MaxZoom && key.X >= 0 && key.Y >= 0 && key.X < (1 << key.Zoom) && key.Y < (1 << key.Zoom);

    private async Task LoadAsync(TileKey key)
    {
        try
        {
            byte[]? bytes = ReadFromDisk(key);
            if (bytes is null)
            {
                await downloads.WaitAsync();
                try
                {
                    // The view may have moved on while this waited for a download slot.
                    if (!IsWanted(key))
                    {
                        return;
                    }

                    bytes = await DownloadAsync(key);
                }
                finally
                {
                    downloads.Release();
                }
            }

            using MemoryStream stream = new(bytes);
            Bitmap bitmap = new(stream);
            lock (gate)
            {
                Remember(key, bitmap);
            }

            Raise();
        }
        catch (Exception)
        {
            // Offline, blocked, a bad tile: the map just stays without it. It is tried again after a short pause.
            lock (gate)
            {
                failed[key] = DateTime.UtcNow;
            }
        }
        finally
        {
            lock (gate)
            {
                loading.Remove(key);
            }
        }
    }

    private bool IsWanted(TileKey key)
    {
        lock (gate)
        {
            return wanted.Count == 0 || wanted.Contains(key);
        }
    }

    private async Task<byte[]> DownloadAsync(TileKey key)
    {
        string url = urlTemplate
            .Replace("{z}", key.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{x}", key.X.ToString(System.Globalization.CultureInfo.InvariantCulture))
            .Replace("{y}", key.Y.ToString(System.Globalization.CultureInfo.InvariantCulture));

        using HttpResponseMessage response = await http.GetAsync(url);
        response.EnsureSuccessStatusCode();
        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
        WriteToDisk(key, bytes);
        return bytes;
    }

    private string? PathFor(TileKey key) =>
        cacheDirectory is null ? null : Path.Combine(cacheDirectory, key.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture), key.X.ToString(System.Globalization.CultureInfo.InvariantCulture), key.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");

    private byte[]? ReadFromDisk(TileKey key)
    {
        string? path = PathFor(key);
        try
        {
            return path is not null && File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < DiskTileLifetime
                ? File.ReadAllBytes(path)
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // The cache is an optimisation: if it cannot be written, carry on without it.
    private void WriteToDisk(TileKey key, byte[] bytes)
    {
        string? path = PathFor(key);
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + "." + Environment.CurrentManagedThreadId + ".tmp";
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private void Remember(TileKey key, Bitmap bitmap)
    {
        if (memory.TryGetValue(key, out LinkedListNode<(TileKey Key, Bitmap Bitmap)>? existing))
        {
            recent.Remove(existing);
        }

        memory[key] = recent.AddFirst((key, bitmap));
        while (memory.Count > memoryCapacity && recent.Last is { } oldest)
        {
            recent.RemoveLast();
            memory.Remove(oldest.Value.Key);
        }
    }

    private void Raise()
    {
        Action? handler = TilesChanged;
        if (handler is null)
        {
            return;
        }

        if (context is null)
        {
            handler();
        }
        else
        {
            context.Post(_ => handler(), null);
        }
    }
}
