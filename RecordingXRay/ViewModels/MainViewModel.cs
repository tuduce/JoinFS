using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RecordingXRay.Services;

namespace RecordingXRay.ViewModels;

public enum StatusKind
{
    Ready,
    Loading,
    Loaded,
    Error,
}

public partial class MainViewModel : ObservableObject
{
    private int loadGeneration;

    public MainViewModel()
        : this(null)
    {
    }

    /// <param name="resolveName">Maps a variable id to its name. Defaults to the JoinFS variable tables, loaded on first use.</param>
    public MainViewModel(Func<uint, string>? resolveName)
    {
        if (resolveName is null)
        {
            Lazy<VariableLookup> lookup = new(VariableLookup.Create);
            resolveName = id => lookup.Value.Resolve(id);
        }

        Browser = new FrameBrowserViewModel();
        Inspector = new InspectorViewModel(resolveName);
        Browser.FrameSelected += Inspector.Show;
    }

    /// <summary>Asks the user for a recording file. Set by the window; returns null when cancelled.</summary>
    public Func<Task<string?>>? PickFile { get; set; }

    /// <summary>Puts text on the clipboard. Set by the window.</summary>
    public Func<string, Task>? CopyText
    {
        get => Inspector.CopyText;
        set => Inspector.CopyText = value;
    }

    public FrameBrowserViewModel Browser { get; }

    public InspectorViewModel Inspector { get; }

    /// <summary>Every aircraft, then every object, of the loaded recording.</summary>
    [ObservableProperty]
    private IReadOnlyList<LaneViewModel> lanes = [];

    [ObservableProperty]
    private LaneViewModel? selectedLane;

    partial void OnSelectedLaneChanged(LaneViewModel? value)
    {
        if (value is null)
        {
            Inspector.Clear();
        }

        Browser.SetLane(value);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecording), nameof(IsEmpty))]
    private RecordingFile? recording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFile), nameof(FileName), nameof(FileDirectory))]
    private string? filePath;

    [ObservableProperty]
    private RecordingSummary summary = RecordingSummary.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(IsStatusReady), nameof(IsStatusLoading), nameof(IsStatusLoaded), nameof(IsStatusError))]
    private StatusKind status = StatusKind.Ready;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand))]
    private bool isLoading;

    [ObservableProperty]
    private IReadOnlyList<SummaryItem> summaryItems = BuildItems(RecordingSummary.Empty);

    public bool HasRecording => Recording is not null;

    public bool IsEmpty => Recording is null;

    public bool HasFile => !string.IsNullOrEmpty(FilePath);

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>Folder part of <see cref="FilePath"/> including the trailing separator, for the address pill.</summary>
    public string FileDirectory
    {
        get
        {
            string? directory = HasFile ? Path.GetDirectoryName(FilePath) : null;
            return string.IsNullOrEmpty(directory)
                ? string.Empty
                : directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        }
    }

    public string FileName => HasFile ? Path.GetFileName(FilePath)! : "No recording open";

    public string StatusText => Status switch
    {
        StatusKind.Loading => "Loading…",
        StatusKind.Loaded => "Recording loaded",
        StatusKind.Error => "Unable to read recording",
        _ => "Ready",
    };

    public bool IsStatusReady => Status == StatusKind.Ready;

    public bool IsStatusLoading => Status == StatusKind.Loading;

    public bool IsStatusLoaded => Status == StatusKind.Loaded;

    public bool IsStatusError => Status == StatusKind.Error;

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        string? path = PickFile is null ? null : await PickFile();
        if (!string.IsNullOrEmpty(path))
        {
            await LoadAsync(path);
        }
    }

    private bool CanOpen() => !IsLoading;

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    /// <summary>
    /// Reads a recording on a background thread. On failure the previously loaded recording stays in place
    /// and the message is shown in the error banner.
    /// </summary>
    public async Task LoadAsync(string path)
    {
        int generation = ++loadGeneration;
        IsLoading = true;
        Status = StatusKind.Loading;
        ErrorMessage = null;

        try
        {
            (RecordingFile file, RecordingSummary summary, LaneViewModel[] laneList) = await Task.Run(() =>
            {
                RecordingFile file = RecordingReader.Read(path);
                LaneViewModel[] laneList = file.Aircraft.Concat<RecordedObject>(file.Objects)
                    .Select(source => new LaneViewModel(source)).ToArray();
                return (file, RecordingSummary.From(file), laneList);
            });

            if (generation != loadGeneration)
            {
                return;
            }

            Recording = file;
            FilePath = path;
            Summary = summary;
            SummaryItems = BuildItems(summary);
            Status = StatusKind.Loaded;

            Inspector.Clear();
            Lanes = laneList;
            SelectedLane = null; // so that loading a recording always starts on its first aircraft
            SelectedLane = laneList.FirstOrDefault();
        }
        catch (Exception ex)
        {
            if (generation != loadGeneration)
            {
                return;
            }

            ErrorMessage = $"{Path.GetFileName(path)}: {ex.Message}";
            Status = StatusKind.Error;
        }
        finally
        {
            if (generation == loadGeneration)
            {
                IsLoading = false;
            }
        }
    }

    internal static IReadOnlyList<SummaryItem> BuildItems(RecordingSummary summary)
    {
        bool placeholder = ReferenceEquals(summary, RecordingSummary.Empty);
        return
        [
            new("VERSION", summary.Version, IsPlaceholder: placeholder),
            new("AIRCRAFT", summary.Aircraft, IsPlaceholder: placeholder),
            new("OBJECTS", summary.Objects, IsPlaceholder: placeholder),
            new("FRAMES", summary.Frames, IsPlaceholder: placeholder),
            new("DURATION", summary.Duration, summary.DurationClock, placeholder),
        ];
    }
}
