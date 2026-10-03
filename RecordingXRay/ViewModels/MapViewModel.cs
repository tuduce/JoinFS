using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

/// <summary>An aircraft drawn on the map at the cursor time.</summary>
/// <param name="HasData">False for the selected aircraft when the cursor is outside its lane: it is held at its nearest end and dimmed.</param>
public sealed record MapMarker(LaneViewModel Lane, MapSample Sample, bool IsSelected, bool HasData);

public readonly record struct GraticuleLine(double Pixel, string Label);

/// <summary>
/// The map: the view (centre and scale), Follow, toggles, and what is drawn at the current cursor. The drawing itself
/// is in <c>MapControl</c>; everything it needs to decide lives here so it can be tested.
/// </summary>
public sealed partial class MapViewModel : ObservableObject
{
    private const double FitPadding = 48;
    private const double WheelStep = 1.25;

    private readonly Action<LaneViewModel>? select;
    private bool needsFit = true;
    private LaneViewModel? selectedLane;

    public MapViewModel(IReadOnlyList<LaneViewModel> lanes, Action<LaneViewModel>? select = null)
    {
        this.select = select;
        Tracks = lanes.Select(MapTrack.Create).OfType<MapTrack>().ToArray();
        centerX = 0.5;
        centerY = 0.5;
        scale = MapProjection.MinScale;
    }

    public static MapViewModel Empty { get; } = new([]);

    /// <summary>Raised whenever something that changes the picture changed.</summary>
    public event Action? Invalidated;

    /// <summary>The path of every lane that has position frames.</summary>
    public IReadOnlyList<MapTrack> Tracks { get; }

    public bool HasTracks => Tracks.Count > 0;

    public double ViewportWidth { get; private set; }

    public double ViewportHeight { get; private set; }

    /// <summary>World coordinates at the middle of the view.</summary>
    [ObservableProperty]
    private double centerX;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaleBarText), nameof(ScaleBarWidth))]
    private double centerY;

    /// <summary>Pixels per world unit.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScaleBarText), nameof(ScaleBarWidth))]
    private double scale;

    [ObservableProperty]
    private bool showTrails = true;

    [ObservableProperty]
    private bool showLabels = true;

    /// <summary>Keep the selected aircraft at the centre. Dragging the map switches it off.</summary>
    [ObservableProperty]
    private bool follow;

    /// <summary>The cursor time, in seconds.</summary>
    public double Time { get; private set; }

    public LaneViewModel? SelectedLane => selectedLane;

    /// <summary>Aircraft to draw at the cursor, the selected one last.</summary>
    public IReadOnlyList<MapMarker> Markers { get; private set; } = [];

    public string CountText { get; private set; } = string.Empty;

    public string TimeText => TimeFormat.Clock(Time);

    public string ReadoutName => selectedLane?.Name ?? string.Empty;

    public bool HasReadout => ReadoutCoordinates.Length > 0;

    /// <summary>Latitude and longitude of the selected aircraft, plus "· no data" outside its lane.</summary>
    public string ReadoutCoordinates { get; private set; } = string.Empty;

    public string ScaleBarText => ScaleBar.Label;

    public double ScaleBarWidth => ScaleBar.Pixels;

    private (double Meters, string Label, double Pixels) ScaleBar =>
        MapProjection.ScaleBar(MapProjection.MetersPerPixel(Scale, MapProjection.LatitudeRadians(CenterY)), 110);

    partial void OnFollowChanged(bool value)
    {
        if (value)
        {
            CenterOnSelected();
        }

        Invalidated?.Invoke();
    }

    partial void OnShowTrailsChanged(bool value) => Invalidated?.Invoke();

    partial void OnShowLabelsChanged(bool value) => Invalidated?.Invoke();

    /// <summary>The size of the drawing area. The first size fits the view to every trail.</summary>
    public void SetViewport(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        bool changed = width != ViewportWidth || height != ViewportHeight;
        ViewportWidth = width;
        ViewportHeight = height;

        if (needsFit)
        {
            Fit();
        }
        else if (changed)
        {
            Invalidated?.Invoke();
        }
    }

    /// <summary>Called by the owner when the cursor or the selected aircraft changes.</summary>
    public void Update(LaneViewModel? selected, double time)
    {
        selectedLane = selected;
        Time = time;

        List<MapMarker> markers = [];
        int withData = 0;
        MapMarker? selectedMarker = null;
        foreach (MapTrack track in Tracks)
        {
            bool isSelected = ReferenceEquals(track.Lane, selected);
            bool hasData = track.Lane.HasDataAt(time);
            if (hasData)
            {
                withData++;
            }

            if (!hasData && !isSelected)
            {
                continue;
            }

            MapMarker marker = new(track.Lane, track.SampleAt(time), isSelected, hasData);
            if (isSelected)
            {
                selectedMarker = marker;
            }
            else
            {
                markers.Add(marker);
            }
        }

        if (selectedMarker is not null)
        {
            markers.Add(selectedMarker);
        }

        Markers = markers;
        string noun = Tracks.All(track => track.Lane.IsAircraft) ? "aircraft" : "tracks";
        CountText = withData == Tracks.Count ? $"{Tracks.Count} {noun}" : $"{withData} of {Tracks.Count} {noun}";
        ReadoutCoordinates = selectedMarker is { } m
            ? MapProjection.Coordinates(m.Sample.Latitude, m.Sample.Longitude) + (m.HasData ? string.Empty : " · no data")
            : string.Empty;

        OnPropertyChanged(nameof(Markers));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(TimeText));
        OnPropertyChanged(nameof(ReadoutName));
        OnPropertyChanged(nameof(ReadoutCoordinates));
        OnPropertyChanged(nameof(HasReadout));
        OnPropertyChanged(nameof(SelectedLane));

        if (Follow)
        {
            CenterOnSelected();
        }

        Invalidated?.Invoke();
    }

    /// <summary>The user clicked a marker: select that aircraft. The view does not move unless Follow is on.</summary>
    public void Pick(LaneViewModel lane) => select?.Invoke(lane);

    /// <summary>Drags the map by a number of pixels. Panning by hand switches Follow off, starting from the current view.</summary>
    public void PanBy(double dx, double dy)
    {
        if ((dx == 0 && dy == 0) || ViewportWidth <= 0)
        {
            return;
        }

        CenterX -= dx / Scale;
        CenterY = Math.Clamp(CenterY - dy / Scale, 0, 1);
        needsFit = false;
        SetFollowWithoutRecentering(false);
        Invalidated?.Invoke();
    }

    /// <summary>Zooms by <paramref name="factor"/> keeping the world point under the pixel (x, y) in place; while following, around the centre.</summary>
    public void ZoomBy(double factor, double x, double y)
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return; // nothing to zoom before the first fit
        }

        if (Follow)
        {
            x = ViewportWidth / 2;
            y = ViewportHeight / 2;
        }

        double newScale = Math.Clamp(Scale * factor, MapProjection.MinScale, MapProjection.MaxScale);
        double worldX = (x - ViewportWidth / 2) / Scale + CenterX;
        double worldY = (y - ViewportHeight / 2) / Scale + CenterY;
        Scale = newScale;
        CenterX = worldX - (x - ViewportWidth / 2) / newScale;
        CenterY = Math.Clamp(worldY - (y - ViewportHeight / 2) / newScale, 0, 1);
        needsFit = false;
        Invalidated?.Invoke();
    }

    public void ZoomWheel(double wheelSteps, double x, double y) => ZoomBy(Math.Pow(WheelStep, wheelSteps), x, y);

    [RelayCommand]
    private void ZoomIn() => ZoomBy(2, ViewportWidth / 2, ViewportHeight / 2);

    [RelayCommand]
    private void ZoomOut() => ZoomBy(0.5, ViewportWidth / 2, ViewportHeight / 2);

    /// <summary>Shows every trail and switches Follow off.</summary>
    [RelayCommand]
    public void FitAll()
    {
        SetFollowWithoutRecentering(false);
        Fit();
    }

    private void Fit()
    {
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return;
        }

        needsFit = false;
        if (!HasTracks)
        {
            CenterX = 0.5;
            CenterY = 0.5;
            Scale = MapProjection.MinScale;
            Invalidated?.Invoke();
            return;
        }

        double minX = Tracks.Min(t => t.MinX);
        double maxX = Tracks.Max(t => t.MaxX);
        double minY = Tracks.Min(t => t.MinY);
        double maxY = Tracks.Max(t => t.MaxY);
        double width = Math.Max(maxX - minX, 1e-12);
        double height = Math.Max(maxY - minY, 1e-12);

        double fit = Math.Min(
            Math.Max(ViewportWidth - 2 * FitPadding, 1) / width,
            Math.Max(ViewportHeight - 2 * FitPadding, 1) / height);

        // A lone point (or a tiny area) would zoom in without limit: stop at about 4 m per pixel.
        double lat = MapProjection.LatitudeRadians((minY + maxY) / 2);
        double closest = MapProjection.EarthCircumferenceMeters * Math.Cos(lat) / 4;

        Scale = Math.Clamp(Math.Min(fit, closest), MapProjection.MinScale, MapProjection.MaxScale);
        CenterX = (minX + maxX) / 2;
        CenterY = (minY + maxY) / 2;
        Invalidated?.Invoke();
    }

    private void CenterOnSelected()
    {
        if (Markers.LastOrDefault(marker => marker.IsSelected) is { } marker)
        {
            CenterX = marker.Sample.X;
            CenterY = marker.Sample.Y;
            needsFit = false;
        }
    }

    // Switches Follow off without the recentre that switching it on does, and without a loop through OnFollowChanged.
    private void SetFollowWithoutRecentering(bool value)
    {
        if (Follow != value)
        {
            Follow = value;
        }
    }

    public double ToScreenX(double worldX) => (worldX - CenterX) * Scale + ViewportWidth / 2;

    public double ToScreenY(double worldY) => (worldY - CenterY) * Scale + ViewportHeight / 2;

    /// <summary>The graticule lines (longitudes, latitudes) that fall in the view, as pixel positions with labels.</summary>
    public (IReadOnlyList<GraticuleLine> Longitudes, IReadOnlyList<GraticuleLine> Latitudes) Graticule(double targetPixels = 90)
    {
        List<GraticuleLine> longitudes = [];
        List<GraticuleLine> latitudes = [];
        if (ViewportWidth <= 0 || ViewportHeight <= 0)
        {
            return (longitudes, latitudes);
        }

        double centerLatitude = MapProjection.LatitudeRadians(CenterY);
        double degreesPerPixel = 360.0 / Scale;

        double lonStep = MapProjection.NiceAngleStep(degreesPerPixel * targetPixels);
        double west = (CenterX - ViewportWidth / 2 / Scale) * 360 - 180;
        double east = (CenterX + ViewportWidth / 2 / Scale) * 360 - 180;
        for (double lon = Math.Ceiling(west / lonStep) * lonStep; lon <= east && longitudes.Count < 200; lon += lonStep)
        {
            double rounded = Math.Round(lon / lonStep) * lonStep;
            longitudes.Add(new GraticuleLine(ToScreenX((rounded + 180) / 360), MapProjection.AngleLabel(rounded, lonStep, "E", "W")));
        }

        double latStep = MapProjection.NiceAngleStep(degreesPerPixel * Math.Cos(centerLatitude) * targetPixels);
        double north = MapProjection.LatitudeRadians(CenterY - ViewportHeight / 2 / Scale) * 180 / Math.PI;
        double south = MapProjection.LatitudeRadians(CenterY + ViewportHeight / 2 / Scale) * 180 / Math.PI;
        for (double lat = Math.Ceiling(south / latStep) * latStep; lat <= north && latitudes.Count < 200; lat += latStep)
        {
            double rounded = Math.Round(lat / latStep) * latStep;
            latitudes.Add(new GraticuleLine(ToScreenY(MapProjection.WorldY(rounded * Math.PI / 180)), MapProjection.AngleLabel(rounded, latStep, "N", "S")));
        }

        return (longitudes, latitudes);
    }
}
