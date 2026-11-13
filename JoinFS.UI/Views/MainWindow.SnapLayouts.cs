using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Win32;

namespace JoinFS.UI.Views;

// Windows 11 shows its snap-layouts flyout when the mouse rests on a window's maximise button, and it finds that button by asking
// the window to hit-test (WM_NCHITTEST). This window draws its own buttons, so the system never sees one. Answering HTMAXBUTTON
// over our maximise button brings the flyout back. The system then owns the mouse there: the pointer events stop reaching Avalonia
// (so hover and click are done here, from the non-client messages) and a snap chosen in the flyout is applied by the system itself.
public partial class MainWindow
{
    private const uint WM_NCHITTEST = 0x0084, WM_NCMOUSEMOVE = 0x00A0, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2,
        WM_NCMOUSELEAVE = 0x02A2;
    private const int HTMAXBUTTON = 9;
    private const uint TME_LEAVE = 0x2, TME_NONCLIENT = 0x10;

    private bool _maxHover, _maxPressed;

    [StructLayout(LayoutKind.Sequential)]
    private struct TrackMouseEventData
    {
        public uint cbSize, dwFlags;
        public IntPtr hwndTrack;
        public uint dwHoverTime;
    }

    [DllImport("user32.dll")]
    private static extern bool TrackMouseEvent(ref TrackMouseEventData data);

    private void InstallSnapLayouts()
    {
        if (OperatingSystem.IsWindows())
            Win32Properties.AddWndProcHookCallback(this, SnapLayoutsWndProc);
    }

    private IntPtr SnapLayoutsWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_NCHITTEST:
                if (IsOverMaximizeButton(lParam))
                {
                    handled = true;
                    return HTMAXBUTTON;
                }
                // the pointer left the button for the client area or another part of the frame
                SetMaximizeState(hover: false, pressed: false);
                break;

            case WM_NCMOUSEMOVE when (int)wParam == HTMAXBUTTON:
                if (!_maxHover)
                {
                    // ask for WM_NCMOUSELEAVE, or the hover look would stay when the pointer goes
                    TrackMouseEventData track = new() { cbSize = (uint)Marshal.SizeOf<TrackMouseEventData>(), dwFlags = TME_LEAVE | TME_NONCLIENT, hwndTrack = hWnd };
                    TrackMouseEvent(ref track);
                }
                SetMaximizeState(hover: true, pressed: _maxPressed);
                handled = true;
                return IntPtr.Zero;

            case WM_NCMOUSELEAVE:
                SetMaximizeState(hover: false, pressed: false);
                break;

            case WM_NCLBUTTONDOWN when (int)wParam == HTMAXBUTTON:
                SetMaximizeState(hover: true, pressed: true);
                handled = true;
                return IntPtr.Zero;

            case WM_NCLBUTTONUP when (int)wParam == HTMAXBUTTON:
                bool click = _maxPressed;
                SetMaximizeState(hover: true, pressed: false);
                if (click)
                    ToggleMaximize();
                handled = true;
                return IntPtr.Zero;
        }
        return IntPtr.Zero;
    }

    // The button is only a maximise button while it is enabled (the full view, resizable and not mid-animation).
    private bool IsOverMaximizeButton(IntPtr lParam)
    {
        if (this.FindControl<Button>("MaximizeButton") is not { IsEnabled: true, IsEffectivelyVisible: true } button || !CanResize)
            return false;

        // screen pixels; the two words are signed, as the pointer can be on a screen to the left of or above the primary one
        int x = (short)(lParam.ToInt64() & 0xFFFF), y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        Point local = this.PointToClient(new PixelPoint(x, y));
        return button.TranslatePoint(new Point(0, 0), this) is { } origin
            && new Rect(origin, button.Bounds.Size).Contains(local);
    }

    // The button gets the look of a pointer over it. A press only counts as a click if it is let go over the button.
    private void SetMaximizeState(bool hover, bool pressed)
    {
        _maxPressed = pressed;
        if (hover == _maxHover || this.FindControl<Button>("MaximizeButton") is not { } button)
            return;

        _maxHover = hover;
        ((IPseudoClasses)button.Classes).Set(":pointerover", hover);
    }
}
