using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.ViewModels.Tabs;

public enum RecorderMode { Idle, Recording, Playing, Overdubbing }

/// <summary>An aircraft of the Aircraft tab, in the Recorder's "Aircraft to record" list, with the tick that includes it.</summary>
public sealed partial class RecordItemViewModel : ObservableObject
{
    private readonly RecordFlag _flag;

    public RecordItemViewModel(RecordedAircraft aircraft, RecordFlag flag)
    {
        _callsign = aircraft.Callsign;
        _model = aircraft.Model;
        _flag = flag;
        _flag.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsChecked));
    }

    [ObservableProperty]
    private string _callsign;

    [ObservableProperty]
    private string _model;

    public bool IsChecked
    {
        get => _flag.IsOn;
        set => _flag.IsOn = value;
    }
}

/// <summary>
/// An aircraft of the loaded recording, with the tick that says it plays. Unticking leaves it out of playback, and for now that is final:
/// the tick goes grey and cannot be set again until the recording is loaded again. Because of that, unticking asks first.
/// </summary>
public sealed partial class LoadedAircraftViewModel : ObservableObject
{
    private readonly Action<string> _skip;
    private readonly Func<LoadedAircraftViewModel, Task<bool>> _confirm;
    private bool _skipped;
    private bool _asking;

    public LoadedAircraftViewModel(RecordedAircraft aircraft, Action<string> skip, Func<LoadedAircraftViewModel, Task<bool>> confirm)
    {
        Id = aircraft.Id;
        Callsign = aircraft.Callsign;
        Model = aircraft.Model;
        _skipped = aircraft.Skipped;
        _skip = skip;
        _confirm = confirm;
    }

    public string Id { get; }
    public string Callsign { get; }
    public string Model { get; }

    /// <summary>Done when the question asked by the last unticking has been answered (for tests; nobody else waits for it).</summary>
    internal Task Answered { get; private set; } = Task.CompletedTask;

    /// <summary>
    /// Ticked while the aircraft plays. Unticking asks whether to leave it out; on yes it is skipped, and ticking it again does nothing.
    /// TODO(newui-review): let an aircraft that was skipped be ticked again, so it joins in playback once more. Postponed: it has to start
    /// again at the right point of the take, which the recorder cannot do yet.
    /// </summary>
    public bool IsChecked
    {
        // while the question is open the box shows what the user did (unticked), so that going back to ticked is a change it takes
        get => !_skipped && !_asking;
        set
        {
            if (value || _skipped || _asking)
                return;

            Answered = LeaveOutAsync();
        }
    }

    private async Task LeaveOutAsync()
    {
        _asking = true;
        OnPropertyChanged(nameof(IsChecked));
        try
        {
            if (await _confirm(this))
            {
                _skipped = true;
                _skip(Id);
            }
        }
        finally
        {
            _asking = false;
            OnPropertyChanged(nameof(IsChecked));
            OnPropertyChanged(nameof(CanTick));
            OnPropertyChanged(nameof(Tip));
        }
    }

    /// <summary>False once the aircraft is skipped: the tick is grey.</summary>
    public bool CanTick => !_skipped;

    /// <summary>Says why the tick is grey; null while it can be used.</summary>
    public string? Tip => _skipped ? Loc.T("Left out of playback. Load the recording again to bring it back.") : null;

    /// <summary>Takes the skipped state the recorder reports, which can be so after a refresh, not only after a click.</summary>
    internal void Update(RecordedAircraft aircraft)
    {
        if (aircraft.Skipped == _skipped)
            return;

        _skipped = aircraft.Skipped;
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(CanTick));
        OnPropertyChanged(nameof(Tip));
    }
}

/// <summary>
/// Recorder tab. The transport is a single-active-mode machine: record, play and overdub switch the other off,
/// and Stop clears all three. Overdub is recording on top of the existing take, so it lights Record as well.
/// The mode, the time and the lists are what the recorder reports, read again while the tab is shown.
/// </summary>
public sealed partial class RecorderViewModel : ObservableObject
{
    private const string SuggestedName = "recording.jfs";
    private const string Extension = "jfs";

    private readonly IRecorderSource _source;
    private readonly ITrafficSource _traffic;
    private readonly RecordSelection _selection;
    private readonly IPlatform _platform;
    private readonly IShell _shell;
    private readonly Dictionary<string, RecordItemViewModel> _liveById = [];
    private readonly Dictionary<string, LoadedAircraftViewModel> _loadedById = [];

    // True while the loop switch is being filled from the recorder, so what it reads is not written back.
    private bool _syncingLoop;
    private bool _unsaved;
    private RecorderStatus _state = new(false, false, false, true, 0, 0);

    public RecorderViewModel(IRecorderSource source, ITrafficSource traffic, RecordSelection recordSelection, IPlatform platform, IShell shell, ShortcutHints hints)
    {
        Hints = hints;
        _source = source;
        _traffic = traffic;
        _selection = recordSelection;
        _platform = platform;
        _shell = shell;
        Refresh();
    }

    /// <summary>The keys of the shortcuts, for the buttons' tooltips.</summary>
    public ShortcutHints Hints { get; }

    /// <summary>The "Aircraft to record" list: the aircraft of the Aircraft tab, with the same ticks.</summary>
    public ObservableCollection<RecordItemViewModel> LiveAircraft { get; } = [];

    /// <summary>The aircraft of the recording that is loaded.</summary>
    public ObservableCollection<LoadedAircraftViewModel> LoadedAircraft { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRecording), nameof(IsPlaying), nameof(IsOverdubbing), nameof(CanSeek))]
    private RecorderMode _mode = RecorderMode.Idle;

    public bool IsRecording => Mode is RecorderMode.Recording or RecorderMode.Overdubbing;

    /// <summary>Playing, and not paused: a paused take looks as if it were stopped, and pressing play goes on.</summary>
    public bool IsPlaying => Mode is RecorderMode.Playing && !_state.Paused;

    public bool IsOverdubbing => Mode == RecorderMode.Overdubbing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LoadedTitle))]
    private string _loadedRecordingName = "";

    public string LoadedTitle
    {
        get
        {
            string what = LoadedRecordingName.Length > 0 ? LoadedRecordingName : _state.Empty ? Loc.T("nothing recorded") : Loc.T("not saved");
            return Loc.F("Loaded recording — {0}", what);
        }
    }

    [ObservableProperty]
    private bool _loopEnabled;

    partial void OnLoopEnabledChanged(bool value)
    {
        if (!_syncingLoop)
            _source.Loop = value;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayheadFraction), nameof(TotalText))]
    private int _totalSeconds;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlayheadFraction), nameof(PlayheadText))]
    private int _playheadSeconds;

    /// <summary>What the last button did, when it did not work or needs saying. Empty otherwise.</summary>
    [ObservableProperty]
    private string _status = "";

    /// <summary>0..1, where the playhead sits on the timeline. Setting it seeks, which is how the seek bar drives it.</summary>
    public double PlayheadFraction
    {
        get => TotalSeconds > 0 ? (double)PlayheadSeconds / TotalSeconds : 0;
        set => Seek(value);
    }

    public string PlayheadText => FormatTime(PlayheadSeconds);
    public string TotalText => FormatTime(TotalSeconds);

    /// <summary>The playhead only means something while a take plays, or is paused: that is when it can be moved and used to trim.</summary>
    public bool CanSeek => Mode == RecorderMode.Playing;

    /// <summary>The recording was made, or laid over, and not saved since.</summary>
    public bool HasUnsavedRecording => _unsaved && !_state.Empty;

    /// <summary>Reads what the recorder is doing: the mode, the time, and both lists. Lists are updated in place.</summary>
    public void Refresh()
    {
        _state = _source.GetStatus();

        Mode = _state.Recording && _state.Playing ? RecorderMode.Overdubbing
            : _state.Recording ? RecorderMode.Recording
            : _state.Playing ? RecorderMode.Playing
            : RecorderMode.Idle;
        OnPropertyChanged(nameof(IsPlaying));

        TotalSeconds = (int)_state.EndTime;
        // while recording the end is where it is; otherwise the playhead stays within the take
        PlayheadSeconds = _state.Recording ? (int)_state.Time : Math.Min((int)_state.Time, TotalSeconds);
        LoadedRecordingName = _source.LoadedRecordingName;
        OnPropertyChanged(nameof(LoadedTitle));

        _syncingLoop = true;
        try
        {
            LoopEnabled = _source.Loop;
        }
        finally
        {
            _syncingLoop = false;
        }

        SyncLive();
        SyncLoaded();

        ToggleRecordingCommand.NotifyCanExecuteChanged();
        TogglePlayCommand.NotifyCanExecuteChanged();
        ToggleOverdubCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        TrimStartCommand.NotifyCanExecuteChanged();
        TrimEndCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(HasUnsavedRecording));
    }

    private void SyncLive()
    {
        List<RecordItemViewModel> wanted = [];
        HashSet<string> seen = [];
        foreach (AircraftInfo aircraft in _traffic.GetAircraft())
        {
            // an aircraft that is played back, or that cannot be recorded for another reason, is not one to choose
            if (!aircraft.Can.HasFlag(AircraftActions.Record) || !seen.Add(aircraft.Id))
                continue;
            if (_liveById.TryGetValue(aircraft.Id, out RecordItemViewModel? item))
            {
                item.Callsign = aircraft.Callsign;
                item.Model = aircraft.Model;
            }
            else
            {
                _liveById[aircraft.Id] = item = new RecordItemViewModel(new RecordedAircraft(aircraft.Callsign, aircraft.Model), _selection.For(aircraft.Id));
            }
            wanted.Add(item);
        }

        foreach (string gone in _liveById.Keys.Except(seen).ToList())
            _liveById.Remove(gone);
        CollectionSync.Reconcile(LiveAircraft, wanted);
    }

    private void SyncLoaded()
    {
        List<LoadedAircraftViewModel> wanted = [];
        HashSet<string> seen = [];
        foreach (RecordedAircraft aircraft in _source.GetLoadedRecording())
        {
            if (!seen.Add(aircraft.Id))
                continue;
            if (_loadedById.TryGetValue(aircraft.Id, out LoadedAircraftViewModel? item))
                item.Update(aircraft);
            else
                _loadedById[aircraft.Id] = item = new LoadedAircraftViewModel(aircraft, _source.SkipAircraft, ConfirmLeaveOutAsync);
            wanted.Add(item);
        }

        foreach (string gone in _loadedById.Keys.Except(seen).ToList())
            _loadedById.Remove(gone);
        CollectionSync.Reconcile(LoadedAircraft, wanted);
    }

    // ---- the transport

    private bool Empty => _state.Empty;

    /// <summary>
    /// Records afresh. Another recording that was not saved is asked about first, as the old window did. Recording replaces playing
    /// and overdubbing, and stops the recording that is on.
    /// </summary>
    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        if (Mode == RecorderMode.Recording)
        {
            _source.Stop();
            Refresh();
            return;
        }

        if (!await AskToSaveAsync())
            return;

        if (Mode != RecorderMode.Idle)
            _source.Stop();
        _source.Record();
        _unsaved = true;
        Status = "";
        Refresh();
    }

    // ---- the shortcuts
    // They follow the rules of the old window's hotkeys: what a button would have disabled does nothing, a recording that is running is
    // stopped and saved by the next one, an overdub of nothing is a plain recording, and no dialog is asked (they are used in VR).
    // The tab is not refreshed while it is hidden, so each starts by reading the recorder.

    /// <summary>Record shortcut: a recording that is running is stopped (and saved), and a new one starts. Nothing while a take plays.</summary>
    public void HotkeyRecord()
    {
        Refresh();
        if (IsRecording)
        {
            _source.Stop();
            Refresh();
        }
        if (_state.Active)
            return;

        StartRecordingWithoutAsking();
    }

    /// <summary>Overdub shortcut: a recording that is running is stopped, then the take is recorded on top of. Without a take it records.</summary>
    public void HotkeyOverdub()
    {
        Refresh();
        if (IsRecording)
        {
            _source.Stop();
            Refresh();
        }

        // overdubbing nothing is recording
        if (Empty)
        {
            if (!_state.Active)
                StartRecordingWithoutAsking();
            return;
        }

        ToggleOverdub();
    }

    public void HotkeyStop()
    {
        Refresh();
        if (CanStop)
            Stop();
    }

    public void HotkeyReplay()
    {
        Refresh();
        if (CanPlay)
            TogglePlay();
    }

    /// <summary>Starts a new recording. A recording that was not saved is saved first, under a name of its own; if that fails, nothing is started.</summary>
    private void StartRecordingWithoutAsking()
    {
        Status = "";
        if (HasUnsavedRecording)
        {
            try
            {
                Status = Loc.F("Recording saved: {0}", _source.AutoSave());
                _unsaved = false;
            }
            catch (Exception ex)
            {
                // the recording stays where it is; the user has to know that nothing was started
                Status = Loc.F("The previous recording was not saved: {0}. No new recording was started.", ex.Message);
                _shell.ShowOverlay(new MessageViewModel(Loc.T("Recorder"), Status));
                return;
            }
        }

        _source.Record();
        _unsaved = true;
        Refresh();
    }

    private bool CanPlay => !Empty && Mode != RecorderMode.Recording;

    /// <summary>Plays; while it plays, pauses; while it is paused, goes on.</summary>
    [RelayCommand(CanExecute = nameof(CanPlay))]
    private void TogglePlay()
    {
        _source.TogglePlay();
        Refresh();
    }

    private bool CanOverdub => Mode == RecorderMode.Overdubbing || (!Empty && Mode != RecorderMode.Recording);

    [RelayCommand(CanExecute = nameof(CanOverdub))]
    private void ToggleOverdub()
    {
        if (Mode == RecorderMode.Overdubbing)
        {
            _source.Stop();
        }
        else
        {
            _source.Overdub();
            _unsaved = true;
        }
        Refresh();
    }

    private bool CanStop => Mode != RecorderMode.Idle;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void Stop()
    {
        _source.Stop();
        Refresh();
    }

    [RelayCommand]
    private void ToggleLoop() => LoopEnabled = !LoopEnabled;

    /// <summary>Moves the playhead to a point on the timeline: click, or drag with the button held. Only while a take plays or is paused.</summary>
    /// <param name="fraction">0..1 along the bar. Anything outside that is clamped.</param>
    public void Seek(double fraction)
    {
        if (!CanSeek)
            return;

        int seconds = (int)Math.Round(Math.Clamp(fraction, 0, 1) * TotalSeconds, MidpointRounding.AwayFromZero);
        PlayheadSeconds = seconds;
        _source.Seek(seconds);
    }

    /// <summary>Cuts everything before the playhead.</summary>
    [RelayCommand(CanExecute = nameof(CanSeek))]
    private void TrimStart()
    {
        if (_state.Time <= 0)
        {
            Status = Loc.T("Already at the start of the recording.");
            return;
        }

        Status = "";
        _source.TrimStart();
        Refresh();
    }

    /// <summary>Cuts everything after the playhead.</summary>
    [RelayCommand(CanExecute = nameof(CanSeek))]
    private void TrimEnd()
    {
        if (_state.Time >= _state.EndTime)
        {
            Status = Loc.T("Already at the end of the recording.");
            return;
        }

        Status = "";
        _source.TrimEnd();
        Refresh();
    }

    // ---- files

    private static string ActiveMessage => Loc.T("The recorder is active. Stop it first.");

    [RelayCommand]
    private Task OpenAsync() => LoadAsync(append: false);

    [RelayCommand]
    private Task AddAsync() => LoadAsync(append: true);

    private async Task LoadAsync(bool append)
    {
        if (Mode != RecorderMode.Idle)
        {
            Status = ActiveMessage;
            return;
        }

        // Opening a file replaces the recording, so one that was not saved is asked about first.
        if (!append && !await AskToSaveAsync())
            return;

        string? path = await _platform.PickOpenFileAsync(append ? Loc.T("Add recording") : Loc.T("Open recording"), _source.RecordingFolder, Extension);
        if (path is null)
            return;

        try
        {
            await _source.OpenAsync(path, append);
            // A file that was opened is what it is on disk; one that was added to the recording changes it.
            _unsaved = append;
            Status = "";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        Refresh();
    }

    [RelayCommand]
    private async Task SaveAsync() => _ = await SaveCoreAsync();

    /// <summary>Asks where to save, and saves. False when it was not saved: the recorder is busy or empty, the dialog was cancelled, or it failed.</summary>
    private async Task<bool> SaveCoreAsync()
    {
        if (Mode != RecorderMode.Idle)
        {
            Status = ActiveMessage;
            return false;
        }
        if (Empty)
        {
            Status = Loc.T("The recorder is empty. Record something first.");
            return false;
        }

        string? path = await _platform.PickSaveFileAsync(Loc.T("Save recording"), SuggestedName, _source.RecordingFolder, Extension);
        if (path is null)
            return false;

        try
        {
            await _source.SaveAsync(path);
        }
        catch (Exception ex)
        {
            Status = Loc.F("The recording was not saved: {0}", ex.Message);
            return false;
        }

        _unsaved = false;
        Status = "";
        Refresh();
        return true;
    }

    /// <summary>Unticking an aircraft of the loaded recording cannot be undone until the recording is loaded again, so the user is asked.</summary>
    private async Task<bool> ConfirmLeaveOutAsync(LoadedAircraftViewModel aircraft)
    {
        ConfirmViewModel ask = new(Loc.T("Leave out of playback?"),
            Loc.F("{0} will not play until the recording is loaded again. It stays in the file.", aircraft.Callsign), Loc.T("Leave Out"), "");
        _shell.ShowOverlay(ask);
        return await ask.Result == ConfirmChoice.Yes;
    }

    /// <summary>
    /// Offers to save a recording that was not saved, before it is lost: when a new one starts, and when the window closes.
    /// True when it is safe to go on: there was nothing to save, it was saved, or the user does not want it. False when they changed their mind.
    /// </summary>
    public async Task<bool> AskToSaveAsync()
    {
        if (!HasUnsavedRecording)
            return true;

        ConfirmViewModel ask = new(Loc.T("Unsaved Recording"), Loc.T("Would you like to save your current recording?"), Loc.T("Save"), Loc.T("Don't Save"));
        _shell.ShowOverlay(ask);
        return await ask.Result switch
        {
            ConfirmChoice.Yes => await SaveCoreAsync(),
            ConfirmChoice.No => true,
            _ => false,
        };
    }

    /// <summary>hh:mm:ss, like the prototype's readout.</summary>
    public static string FormatTime(int seconds)
    {
        int s = Math.Max(0, seconds);
        return string.Create(CultureInfo.InvariantCulture, $"{s / 3600:00}:{s % 3600 / 60:00}:{s % 60:00}");
    }
}
