using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.Services;
using JoinFS.UI.ViewModels.Overlays;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.ViewModels;

/// <summary>How a shortcut's keys are written for the user, and what a shortcut is called.</summary>
public static class ShortcutText
{
    /// <summary>"CTRL+SHIFT+R" as "Ctrl+Shift+R".</summary>
    public static string Display(string combination)
    {
        string[] parts = combination.Split('+', StringSplitOptions.RemoveEmptyEntries);
        return string.Join("+", parts.Select((part, i) => i == parts.Length - 1 ? part.ToUpperInvariant() : char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant()));
    }

    /// <summary>Ctrl, Shift and Alt in any mix, then one letter A-Z, as the old Shortcuts window allowed.</summary>
    public static bool IsValid(string combination)
    {
        string[] keys = combination.Split('+');
        return keys[^1] is { Length: 1 } letter && letter[0] is >= 'A' and <= 'Z'
            && keys[..^1].All(key => key is "CTRL" or "SHIFT" or "ALT");
    }

    /// <summary>The words the Settings card and the old window used for the action.</summary>
    public static string Label(ShortcutAction action) => action switch
    {
        ShortcutAction.Network => Loc.T("Toggle the network state on/off"),
        ShortcutAction.Simulator => Loc.T("Toggle the simulator state on/off"),
        ShortcutAction.AllowShared => Loc.T("Allow shared cockpit with selected user"),
        ShortcutAction.HandOver => Loc.T("Hand over controls to selected user"),
        ShortcutAction.EnterCockpit => Loc.T("Enter/leave cockpit of selected aircraft"),
        ShortcutAction.Follow => Loc.T("Follow selected aircraft"),
        ShortcutAction.Record => Loc.T("Start recording the selected aircraft"),
        ShortcutAction.Overdub => Loc.T("Start overdub recording"),
        ShortcutAction.Stop => Loc.T("Stop recording/playing"),
        _ => Loc.T("Play/pause replay of the recorded aircraft"),
    };
}

/// <summary>
/// What the buttons say about the shortcuts: a tooltip with the keys, for each button a shortcut does the same as. A shortcut that is off
/// has no tooltip (null), so the button shows none. Raises <c>PropertyChanged</c> for everything when the shortcuts change.
/// </summary>
public sealed class ShortcutHints : ObservableObject
{
    private Dictionary<ShortcutAction, string> _keys = [];

    /// <summary>Takes the shortcuts as they are now.</summary>
    public void Update(IEnumerable<ShortcutBinding> bindings)
    {
        _keys = bindings.Where(b => b.Enabled).ToDictionary(b => b.Action, b => ShortcutText.Display(b.Combination));
        OnPropertyChanged(string.Empty);
    }

    /// <summary>"Shortcut: Ctrl+N", or null when the shortcut is off.</summary>
    public string? Hint(ShortcutAction action) => _keys.TryGetValue(action, out string? keys) ? Loc.F("Shortcut: {0}", keys) : null;

    /// <summary>The button's own words with the keys after them, or just the words when the shortcut is off.</summary>
    private string With(string text, ShortcutAction action) => _keys.TryGetValue(action, out string? keys) ? text + " (" + keys + ")" : text;

    public string? Network => Hint(ShortcutAction.Network);
    public string? Simulator => Hint(ShortcutAction.Simulator);
    public string? AllowShared => Hint(ShortcutAction.AllowShared);
    public string? HandOver => Hint(ShortcutAction.HandOver);
    public string? EnterCockpit => Hint(ShortcutAction.EnterCockpit);
    public string? Follow => Hint(ShortcutAction.Follow);

    public string RecordTip => With(Loc.T("Record"), ShortcutAction.Record);
    public string PlayTip => With(Loc.T("Play"), ShortcutAction.Replay);
    public string StopTip => With(Loc.T("Stop"), ShortcutAction.Stop);
    public string OverdubTip => With(Loc.T("Overdub — record on top of existing take"), ShortcutAction.Overdub);
}

/// <summary>One line of the Keyboard Shortcuts card: a switch, what it does, its keys and a way to change them.</summary>
public sealed partial class ShortcutRowViewModel : ObservableObject
{
    private readonly ShortcutsSettingsViewModel _owner;

    internal ShortcutRowViewModel(ShortcutBinding binding, ShortcutsSettingsViewModel owner)
    {
        Action = binding.Action;
        Label = ShortcutText.Label(binding.Action);
        _owner = owner;
        _enabled = binding.Enabled;
        _combination = binding.Combination;
    }

    public ShortcutAction Action { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _enabled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Keys))]
    private string _combination;

    /// <summary>The keys as they are shown: Ctrl+Shift+R.</summary>
    public string Keys => ShortcutText.Display(Combination);

    partial void OnEnabledChanged(bool value) => _owner.Save(this);

    [RelayCommand]
    private void Change() => _owner.Change(this);
}

/// <summary>
/// The Keyboard Shortcuts card of Settings. The shortcuts are kept as they change, in the settings the old Shortcuts window used, so the
/// old and new UI see each other's choices.
/// </summary>
public sealed class ShortcutsSettingsViewModel : SettingsSectionViewModel
{
    private readonly IShortcutSource _source;
    private readonly IShell _shell;
    private readonly Action<IEnumerable<ShortcutBinding>> _changed;

    internal ShortcutsSettingsViewModel(Action<SettingsSectionViewModel> toggle, IShortcutSource source, IShell shell, Action<IEnumerable<ShortcutBinding>> changed)
        : base(Loc.T("Keyboard Shortcuts"), toggle)
    {
        _source = source;
        _shell = shell;
        _changed = changed;
        Rows = [.. source.Load().Select(b => new ShortcutRowViewModel(b, this))];
    }

    public IReadOnlyList<ShortcutRowViewModel> Rows { get; }

    internal void Save(ShortcutRowViewModel row)
    {
        _source.Save(new ShortcutBinding(row.Action, row.Enabled, row.Combination));
        _changed(Rows.Select(r => new ShortcutBinding(r.Action, r.Enabled, r.Combination)));
    }

    internal void Change(ShortcutRowViewModel row)
    {
        ShortcutCaptureViewModel capture = new(row.Label, row.Combination);
        capture.Accepted += (_, combination) =>
        {
            row.Combination = combination;
            Save(row);
        };
        _shell.ShowOverlay(capture);
    }
}
