namespace JoinFS.UI.Services;

/// <summary>
/// Stamen Design's Watercolor map, as the Smithsonian's Cooper Hewitt museum keeps it online for everyone (CC BY 3.0). The pictures are an
/// archive of the map made from OpenStreetMap data, and go down to zoom 16 only.
/// </summary>
public sealed class WatercolorTileSource(string userAgent, string? cacheFolder, HttpMessageHandler? handler = null)
    : HttpTileSource("https://watercolormaps.collection.cooperhewitt.org/tile/watercolor/{z}/{x}/{y}.jpg", "jpg", "image/jpeg", userAgent, cacheFolder, handler)
{
    public override string Attribution => "Map tiles by Stamen Design, CC BY 3.0 · Data © OpenStreetMap contributors";
    public override string AttributionUrl => "https://watercolormaps.collection.cooperhewitt.org";
    public override int MaxZoom => 16;
}
