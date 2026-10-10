using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia;
using JoinFS.UI.ViewModels.Overlays;

namespace JoinFS.UI.Views.Overlays;

public partial class ShortcutCaptureView : UserControl
{
    public ShortcutCaptureView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // the keys go to whatever has the focus, so it has to be here
        Dispatcher.UIThread.Post(() => Focus(), DispatcherPriority.Input);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Only a letter is a key of a shortcut. Escape, Tab and the rest go on, so the card can still be closed and the buttons reached.
        if (e.Key is >= Key.A and <= Key.Z && DataContext is ShortcutCaptureViewModel capture)
        {
            capture.KeyPressed(
                e.KeyModifiers.HasFlag(KeyModifiers.Control),
                e.KeyModifiers.HasFlag(KeyModifiers.Shift),
                e.KeyModifiers.HasFlag(KeyModifiers.Alt),
                (char)('A' + (e.Key - Key.A)));
            e.Handled = true;
        }
    }
}
