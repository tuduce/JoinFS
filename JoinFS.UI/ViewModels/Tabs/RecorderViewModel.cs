using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Models;
using JoinFS.UI.Services;

namespace JoinFS.UI.ViewModels.Tabs;

public enum RecorderMode { Idle, Recording, Playing, Overdubbing }

/// <summary>An aircraft in one of the Recorder's two lists, with the tick that includes it.</summary>
public sealed class RecordItemViewModel : ObservableObject
{
    private readonly RecordFlag _flag;

    public RecordItemViewModel(RecordedAircraft aircraft, RecordFlag flag)
    {
        Callsign = aircraft.Callsign;
        Model = aircraft.Model;
        _flag = flag;
        _flag.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsChecked));
    }

    public string Callsign { get; }
    public string Model { get; }

    public bool IsChecked
    {
        get => _flag.IsOn;
        set => _flag.IsOn = value;
    }
}

/// <summary>
/// Recorder tab. The transport is a single-active-mode machine: record, play and overdub switch the other off,
/// and Stop clears all three. Overdub is recording on top of the existing take, so it lights Record as well.
/// </summary>
public sealed partial class RecorderViewModel : ObservableObject
{
    private const string SaveSuggestedName = "recording.jfs";

    private readonly IRecorderSource _source;
    private readonly IPlatform _platform;

    public RecorderViewModel(IRecorderSource source, ITrafficSource traffic, RecordSelection recordSelection, IPlatform platform)
    {
        _source = source;
        _platform = platform;
        _loadedRecordingName = source.LoadedRecordingName;
        _totalSeconds = source.LoadedRecordingSeconds;
        _playheadSeconds = 7;

        // "Aircraft to record" lists the aircraft of the Aircraft tab, with the same ticks.
        foreach (AircraftInfo a in traffic.GetAircraft())
            LiveAircraft.Add(new RecordItemViewModel(new RecordedAircraft(a.Callsign, a.Model), recordSelection.For(a.Callsign)));

        // The loaded recording's own ticks (which of its aircraft to play) are separate.
        HashSet<string> loadedTicked = ["6Knotts", "ADF320", "CarGuy86", "DiegoCuervo"];
        foreach (RecordedAircraft a in source.GetLoadedRecording())
            LoadedAircraft.Add(new RecordItemViewModel(a, new RecordFlag { IsOn = loadedTicked.Contains(a.Callsign) }));
    }

    public ObservableCollection<RecordItemViewModel> LiveAircraft { get; } = [];
    public ObservableCollection<RecordItemViewModel> LoadedAircraft { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsPlaying), nameof(IsOverdubbing))]
    private RecorderMode _mode = RecorderMode.Idle;

    public bool IsRecording => Mode is RecorderMode.Recording or RecorderMode.Overdubbing;
    public bool IsPlaying => Mode == RecorderMode.Playing;
    public bool IsOverdubbing => Mode == RecorderMode.Overdubbing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadedTitle))]
    private string _loadedRecordingName;

    public string LoadedTitle => $"Loaded recording — {LoadedRecordingName}";

    [ObservableProperty]
    private bool _loopEnabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayheadFraction), nameof(PlayheadText), nameof(TotalText))]
    private int _totalSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayheadFraction), nameof(PlayheadText))]
    private int _playheadSeconds;

    /// <summary>0..1, where the playhead sits on the timeline. Setting it seeks, which is how the seek bar drives it.</summary>
    public double PlayheadFraction
    {
        get => TotalSeconds > 0 ? (double)PlayheadSeconds / TotalSeconds : 0;
        set => Seek(value);
    }

    public string PlayheadText => FormatTime(PlayheadSeconds);
    public string TotalText => FormatTime(TotalSeconds);

    [RelayCommand]
    private void ToggleRecording() => Mode = Mode == RecorderMode.Recording ? RecorderMode.Idle : RecorderMode.Recording;

    [RelayCommand]
    private void TogglePlay() => Mode = Mode == RecorderMode.Playing ? RecorderMode.Idle : RecorderMode.Playing;

    [RelayCommand]
    private void ToggleOverdub() => Mode = Mode == RecorderMode.Overdubbing ? RecorderMode.Idle : RecorderMode.Overdubbing;

    [RelayCommand]
    private void Stop() => Mode = RecorderMode.Idle;

    [RelayCommand]
    private void ToggleLoop() => LoopEnabled = !LoopEnabled;

    /// <summary>Moves the playhead to a point on the timeline: click, or drag with the button held.</summary>
    /// <param name="fraction">0..1 along the bar. Anything outside that is clamped.</param>
    public void Seek(double fraction) =>
        PlayheadSeconds = (int)Math.Round(Math.Clamp(fraction, 0, 1) * TotalSeconds, MidpointRounding.AwayFromZero);

    /// <summary>Cuts everything before the playhead.</summary>
    [RelayCommand]
    private void TrimStart()
    {
        TotalSeconds = Math.Max(1, TotalSeconds - PlayheadSeconds);
        PlayheadSeconds = 0;
    }

    /// <summary>Cuts everything after the playhead.</summary>
    [RelayCommand]
    private void TrimEnd()
    {
        TotalSeconds = Math.Max(1, PlayheadSeconds);
        PlayheadSeconds = Math.Max(1, PlayheadSeconds);
    }

    // Reading and writing .jfs files belongs to the real recorder; for now the dialogs only name the loaded take.
    [RelayCommand]
    private async Task OpenAsync()
    {
        string? path = await _platform.PickOpenFileAsync("Open recording");
        if (path is not null)
            LoadedRecordingName = Path.GetFileName(path);
    }

    [RelayCommand]
    private async Task AddAsync() => _ = await _platform.PickOpenFileAsync("Add recording");

    [RelayCommand]
    private async Task SaveAsync() => _ = await _platform.PickSaveFileAsync("Save recording", SaveSuggestedName);

    /// <summary>hh:mm:ss, like the prototype's readout.</summary>
    public static string FormatTime(int seconds)
    {
        int s = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}");
    }
}
