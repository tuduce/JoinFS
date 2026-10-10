namespace JoinFS.UI.Services;

/// <summary>The tiles of openstreetmap.org, under its tile usage policy (https://operations.osmfoundation.org/policies/tiles/).</summary>
public sealed class OsmTileSource(string userAgent, string? cacheFolder, HttpMessageHandler? handler = null)
    : HttpTileSource("https://tile.openstreetmap.org/{z}/{x}/{y}.png", "png", "image/png", userAgent, cacheFolder, handler)
{
    public override string Attribution => "© OpenStreetMap contributors";
    public override string AttributionUrl => "https://www.openstreetmap.org/copyright";
    public override int MaxZoom => 19;
}
