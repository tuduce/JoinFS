using Avalonia;

namespace RecordingXRay.Services;

/// <summary>A slippy-map tile: zoom level, then column and row (row 0 at the top).</summary>
public readonly record struct TileKey(int Zoom, int X, int Y)
{
    /// <summary>The tile one zoom level out that contains this one.</summary>
    public TileKey Parent => new(Zoom - 1, X >> 1, Y >> 1);

    public override string ToString() => $"{Zoom}/{X}/{Y}";
}

/// <summary>A tile and where it goes on screen (top-left corner and side length in pixels).</summary>
public readonly record struct TilePlacement(TileKey Key, double X, double Y, double Size);

/// <summary>Which tiles cover a map view, and how to reuse a coarser tile while a finer one loads.</summary>
public static class TileMath
{
    public const int TileSize = 256;

    public const int MaxZoom = 19;

    /// <summary>The tile zoom for a view scale (pixels per world unit): tiles end up between about 180 and 360 pixels wide.</summary>
    public static int ZoomFor(double scale) =>
        Math.Clamp((int)Math.Round(Math.Log2(Math.Max(scale, 1) / TileSize)), 0, MaxZoom);

    /// <summary>The tiles that cover the view. Columns are wrapped round the world; rows are clipped to it.</summary>
    public static IReadOnlyList<TilePlacement> Visible(double centerX, double centerY, double scale, double width, double height, int maxTiles = 400)
    {
        List<TilePlacement> tiles = [];
        if (width <= 0 || height <= 0 || scale <= 0)
        {
            return tiles;
        }

        int zoom = ZoomFor(scale);
        int count = 1 << zoom;
        double size = scale / count;

        int firstColumn = (int)Math.Floor((centerX - width / 2 / scale) * count);
        int lastColumn = (int)Math.Floor((centerX + width / 2 / scale) * count);
        int firstRow = Math.Max(0, (int)Math.Floor((centerY - height / 2 / scale) * count));
        int lastRow = Math.Min(count - 1, (int)Math.Floor((centerY + height / 2 / scale) * count));

        // Nearest the middle of the view first, so those load first.
        for (int row = firstRow; row <= lastRow; row++)
        {
            for (int column = firstColumn; column <= lastColumn; column++)
            {
                int wrapped = ((column % count) + count) % count;
                tiles.Add(new TilePlacement(
                    new TileKey(zoom, wrapped, row),
                    ((double)column / count - centerX) * scale + width / 2,
                    ((double)row / count - centerY) * scale + height / 2,
                    size));
            }
        }

        return tiles
            .OrderBy(t => Math.Abs(t.X + t.Size / 2 - width / 2) + Math.Abs(t.Y + t.Size / 2 - height / 2))
            .Take(maxTiles)
            .ToList();
    }

    /// <summary>
    /// The part of a coarser tile (<paramref name="ancestor"/>, a bitmap of <paramref name="bitmapSize"/> pixels) that shows
    /// the area of <paramref name="tile"/>, or null when it is not an ancestor.
    /// </summary>
    public static Rect? SourceRect(TileKey tile, TileKey ancestor, Size bitmapSize)
    {
        int levels = tile.Zoom - ancestor.Zoom;
        if (levels < 0 || (tile.X >> levels) != ancestor.X || (tile.Y >> levels) != ancestor.Y)
        {
            return null;
        }

        double part = 1.0 / (1 << levels);
        double column = tile.X - ((long)ancestor.X << levels);
        double row = tile.Y - ((long)ancestor.Y << levels);
        return new Rect(column * part * bitmapSize.Width, row * part * bitmapSize.Height, part * bitmapSize.Width, part * bitmapSize.Height);
    }
}
