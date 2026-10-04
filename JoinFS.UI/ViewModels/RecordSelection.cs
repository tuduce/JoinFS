using CommunityToolkit.Mvvm.ComponentModel;

namespace JoinFS.UI.ViewModels;

/// <summary>Whether one aircraft is included in the recording.</summary>
public sealed partial class RecordFlag : ObservableObject
{
    [ObservableProperty]
    private bool _isOn;
}

/// <summary>
/// Which aircraft are recorded. The Aircraft tab's Record column and the Recorder tab's "Aircraft to record" list
/// both show this, so ticking in one ticks in the other.
/// </summary>
public sealed class RecordSelection(IEnumerable<string> initiallyRecorded)
{
    private readonly HashSet<string> _initial = [.. initiallyRecorded];
    private readonly Dictionary<string, RecordFlag> _flags = [];

    /// <summary>The flag of <paramref name="callsign"/>. The same instance every time.</summary>
    public RecordFlag For(string callsign)
    {
        if (!_flags.TryGetValue(callsign, out RecordFlag? flag))
            _flags[callsign] = flag = new RecordFlag { IsOn = _initial.Contains(callsign) };
        return flag;
    }
}
