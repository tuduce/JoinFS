using Avalonia.Media.Imaging;

namespace RecordingXRay.Services;

/// <summary>Where map tiles come from. Loading is asynchronous; <see cref="TilesChanged"/> says a tile has arrived.</summary>
public interface ITileSource
{
    /// <summary>Raised, on the UI thread when there is one, after a tile has loaded.</summary>
    event Action? TilesChanged;

    /// <summary>The credit that must be shown with the map, such as "© OpenStreetMap contributors".</summary>
    string Attribution { get; }

    /// <summary>Where the credit links to.</summary>
    string AttributionUrl { get; }

    /// <summary>The tile if it is ready now, else null.</summary>
    Bitmap? TryGet(TileKey key);

    /// <summary>Starts loading a tile that is not ready. Does nothing if it is already loading or recently failed.</summary>
    void Request(TileKey key);

    /// <summary>The tiles the view needs right now. Queued loads for any other tile are dropped.</summary>
    void SetWanted(IReadOnlyCollection<TileKey> wanted);
}
