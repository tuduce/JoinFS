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
/// both show this, so ticking in one ticks in the other. The Aircraft tab fills it from the live app and writes changes back.
/// </summary>
public sealed class RecordSelection
{
    private readonly Dictionary<string, RecordFlag> _flags = [];

    /// <summary>The flag of the aircraft with this id. The same instance every time.</summary>
    public RecordFlag For(string aircraftId)
    {
        if (!_flags.TryGetValue(aircraftId, out RecordFlag? flag))
            _flags[aircraftId] = flag = new RecordFlag();
        return flag;
    }
}
