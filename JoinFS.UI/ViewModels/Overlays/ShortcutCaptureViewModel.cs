using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using JoinFS.UI.Localization;

namespace JoinFS.UI.ViewModels.Overlays;

/// <summary>
/// Asks for the keys of a shortcut: the user holds Ctrl, Shift and Alt as they like and presses a letter, and the card shows what it got.
/// The view passes the key presses in. Escape and Cancel close it without a change. While it is open the shortcuts do nothing.
/// </summary>
public sealed partial class ShortcutCaptureViewModel(string action, string current) : OverlayViewModel
{
    public override string Title => Loc.T("Change Shortcut");

    /// <summary>What the shortcut does.</summary>
    public string Action => action;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Keys), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _combination = current;

    /// <summary>The keys as they are shown: Ctrl+Shift+R.</summary>
    public string Keys => ShortcutText.Display(Combination);

    public bool CanSave => ShortcutText.IsValid(Combination);

    /// <summary>The user chose these keys.</summary>
    public event EventHandler<string>? Accepted;

    /// <summary>A key went down. Only a letter counts; a modifier alone leaves what is shown.</summary>
    public void KeyPressed(bool control, bool shift, bool alt, char letter)
    {
        letter = char.ToUpperInvariant(letter);
        if (letter is < 'A' or > 'Z')
            return;

        Combination = (control ? "CTRL+" : "") + (shift ? "SHIFT+" : "") + (alt ? "ALT+" : "") + letter;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        Accepted?.Invoke(this, Combination);
        Close();
    }
}
