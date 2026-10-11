using Avalonia.Media.Imaging;
using Avalonia.Threading;
using JoinFS.UI.Services;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// The decoded tiles the map has on hand. Asking for a tile that is not here starts fetching it and answers null for now; the
/// <c>loaded</c> callback (on the UI thread) says when to ask again. Only tiles the last frame wanted are fetched, so a fast zoom
/// does not leave a queue of tiles nobody looks at.
/// </summary>
internal sealed class MapTileCache : IDisposable
{
    private const int Capacity = 400;
    private static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(30);

    private readonly IMapTileSource _source;
    private readonly Action _loaded;
    private readonly CancellationTokenSource _cancel = new();
    private readonly SemaphoreSlim _gate = new(4);

    // Everything below is touched on the UI thread only.
    private readonly Dictionary<(int Z, int X, int Y), Entry> _tiles = [];
    private HashSet<(int, int, int)> _lastFrame = [];
    private HashSet<(int, int, int)> _thisFrame = [];
    private long _clock;

    private sealed class Entry
    {
        public Bitmap? Bitmap;
        public bool Loading;
        public DateTime FailedAt;
        public long LastUsed;
    }

    public MapTileCache(IMapTileSource source, Action loaded)
    {
        _source = source;
        _loaded = loaded;
    }

    /// <summary>Call after the tiles of a frame were asked for: what neither this frame nor the one before asked for is not fetched any more.</summary>
    public void EndFrame()
    {
        (_lastFrame, _thisFrame) = (_thisFrame, _lastFrame);
        _thisFrame.Clear();
    }

    public Bitmap? Get(int zoom, int x, int y)
    {
        var key = (zoom, x, y);
        _thisFrame.Add(key);

        if (!_tiles.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry();
            _tiles[key] = entry;
        }
        entry.LastUsed = ++_clock;

        if (entry.Bitmap is not null)
            return entry.Bitmap;

        if (!entry.Loading && (entry.FailedAt == default || DateTime.UtcNow - entry.FailedAt > RetryAfter))
        {
            entry.Loading = true;
            _ = LoadAsync(key, entry);
        }

        Trim();
        return null;
    }

    private async Task LoadAsync((int Z, int X, int Y) key, Entry entry)
    {
        Bitmap? bitmap = null;
        try
        {
            await _gate.WaitAsync(_cancel.Token);
            try
            {
                // by now the map may have moved on
                if (!await WantedAsync(key))
                {
                    await Dispatcher.UIThread.InvokeAsync(() => entry.Loading = false);
                    return;
                }

                byte[]? bytes = await _source.GetTileAsync(key.Z, key.X, key.Y, _cancel.Token);
                if (bytes is not null)
                {
                    using MemoryStream stream = new(bytes);
                    bitmap = new Bitmap(stream);
                }
            }
            finally
            {
                _gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // a tile that does not come, or is not an image, is a hole in the map and nothing more
            bitmap = null;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            entry.Loading = false;
            if (bitmap is null)
            {
                entry.FailedAt = DateTime.UtcNow;
            }
            else
            {
                entry.Bitmap = bitmap;
                _loaded();
            }
        });
    }

    private async Task<bool> WantedAsync((int, int, int) key) =>
        await Dispatcher.UIThread.InvokeAsync(() => _thisFrame.Contains(key) || _lastFrame.Contains(key));

    /// <summary>Lets go of the tiles that were looked at least recently, when there are too many.</summary>
    private void Trim()
    {
        if (_tiles.Count <= Capacity)
            return;

        foreach (var old in _tiles.Where(t => !t.Value.Loading).OrderBy(t => t.Value.LastUsed).Take(_tiles.Count - Capacity * 3 / 4).Select(t => t.Key).ToList())
        {
            _tiles[old].Bitmap?.Dispose();
            _tiles.Remove(old);
        }
    }

    public void Dispose()
    {
        _cancel.Cancel();
        foreach (Entry entry in _tiles.Values)
            entry.Bitmap?.Dispose();
        _tiles.Clear();
        _cancel.Dispose();
    }
}
