using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

/// <summary>A toggle chip for one frame kind in the frame browser.</summary>
public sealed partial class TypeChipViewModel : ObservableObject
{
    public TypeChipViewModel(FrameKind kind, Action changed)
    {
        Kind = kind;
        Label = FrameKinds.Label(kind);
        this.changed = changed;
    }

    private readonly Action changed;

    public FrameKind Kind { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool isOn = true;

    partial void OnIsOnChanged(bool value) => changed();
}

/// <summary>The list of frames of the selected lane, with type chips, a text filter and "go to time".</summary>
public sealed partial class FrameBrowserViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLane), nameof(Title), nameof(Subtitle), nameof(CountText))]
    private LaneViewModel? lane;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText))]
    private FrameRowList rows = FrameRowList.Empty;

    [ObservableProperty]
    private ObservableCollection<TypeChipViewModel> typeChips = [];

    [ObservableProperty]
    private string filterText = string.Empty;

    /// <summary>The row selected in the list. Raised only for a real selection; see <see cref="FrameSelected"/>.</summary>
    [ObservableProperty]
    private FrameRow? selectedRow;

    /// <summary>Raised when the user picks a frame (never with null, so filtering does not clear the inspector).</summary>
    public event Action<LaneViewModel, FrameRow>? FrameSelected;

    /// <summary>Raised when the list should scroll a row into view.</summary>
    public event Action<FrameRow>? RevealRequested;

    public bool HasLane => Lane is not null;

    public string Title => Lane?.Name ?? string.Empty;

    public string Subtitle => Lane?.Model ?? string.Empty;

    public string CountText
    {
        get
        {
            if (Lane is null)
            {
                return string.Empty;
            }

            return Rows.Count == Lane.Frames.Count
                ? $"{Lane.FrameCountText} frames"
                : $"{Rows.Count.ToString("N0", CultureInfo.InvariantCulture)} of {Lane.FrameCountText} frames";
        }
    }

    /// <summary>Shows a lane and selects its first frame.</summary>
    public void SetLane(LaneViewModel? newLane)
    {
        if (ReferenceEquals(Lane, newLane))
        {
            return;
        }

        Lane = newLane;
        TypeChips = new ObservableCollection<TypeChipViewModel>(
            (newLane?.Kinds ?? []).Select(kind => new TypeChipViewModel(kind, ApplyFilter)));
        FilterText = string.Empty;
        Rows = newLane is null ? FrameRowList.Empty : new FrameRowList(newLane.Frames, null);
        SelectedRow = null;
        if (Rows.Count > 0)
        {
            Select(Rows[0]);
        }
    }

    /// <summary>Selects a frame by its index in the lane, widening the filter if it hides that frame.</summary>
    public void SelectFrame(int frameIndex)
    {
        if (Lane is null || (uint)frameIndex >= (uint)Lane.Frames.Count)
        {
            return;
        }

        if (Rows.PositionOf(frameIndex) < 0)
        {
            ResetFilter();
        }

        int position = Rows.PositionOf(frameIndex);
        if (position >= 0)
        {
            Select(Rows[position]);
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnSelectedRowChanged(FrameRow? value)
    {
        if (value is not null && Lane is not null)
        {
            FrameSelected?.Invoke(Lane, value);
        }
    }

    /// <summary>Enter in the filter box: when the text is a time, select the first frame at or after it.</summary>
    [RelayCommand]
    private void GoToTime()
    {
        if (Rows.Count > 0 && TimeParse.TryParse(FilterText, out double seconds))
        {
            Select(Rows[Rows.PositionAtOrAfter(seconds)]);
        }
    }

    private void Select(FrameRow row)
    {
        SelectedRow = row;
        RevealRequested?.Invoke(row);
    }

    private void ResetFilter()
    {
        FilterText = string.Empty;
        foreach (TypeChipViewModel chip in TypeChips)
        {
            chip.IsOn = true;
        }

        ApplyFilter();
    }

    private void ApplyFilter()
    {
        if (Lane is null)
        {
            return;
        }

        HashSet<FrameKind> visible = TypeChips.Where(chip => chip.IsOn).Select(chip => chip.Kind).ToHashSet();
        bool allKinds = visible.Count == TypeChips.Count;

        // A time ("12.5", "1:02.5") is for "go to", not for filtering.
        string text = TimeParse.TryParse(FilterText, out _) ? string.Empty : FilterText.Trim();

        if (allKinds && text.Length == 0)
        {
            Rows = new FrameRowList(Lane.Frames, null);
            return;
        }

        List<int> indexes = [];
        IReadOnlyList<RecordedFrame> frames = Lane.Frames;
        for (int i = 0; i < frames.Count; i++)
        {
            RecordedFrame frame = frames[i];
            if (visible.Contains(FrameKinds.Of(frame.Type))
                && (text.Length == 0 || frame.Type.ToString().Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                indexes.Add(i);
            }
        }

        Rows = new FrameRowList(frames, indexes.ToArray());
    }
}
