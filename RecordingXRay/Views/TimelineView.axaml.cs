using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views;

public partial class TimelineView : UserControl
{
    private bool scrubbing;

    public TimelineView()
    {
        InitializeComponent();
        LanePanel.PointerPressed += OnPointerPressed;
        LanePanel.PointerMoved += OnPointerMoved;
        LanePanel.PointerReleased += OnPointerReleased;
        LanePanel.PointerCaptureLost += (_, _) => scrubbing = false;
        LanePanel.PointerWheelChanged += OnPointerWheelChanged;
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    // Click or drag on the ruler or a lane to scrub.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Lanes).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        scrubbing = true;
        e.Pointer.Capture(LanePanel);
        ScrubTo(e);
        e.Handled = true;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (scrubbing)
        {
            ScrubTo(e);
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (scrubbing)
        {
            scrubbing = false;
            e.Pointer.Capture(null);
        }
    }

    private void ScrubTo(PointerEventArgs e) => ViewModel?.MoveCursor(Lanes.TimeAt(e.GetPosition(Lanes).X));

    // The wheel over the ruler or the lanes zooms around the pointer; Shift + wheel (or a horizontal wheel) pans.
    // Over the aircraft names the wheel scrolls the tracks, as usual.
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (ViewModel?.Timeline is not { } timeline)
        {
            return;
        }

        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) || e.Delta.X != 0)
        {
            double delta = e.Delta.X != 0 ? e.Delta.X : e.Delta.Y;
            timeline.Pan(-delta * timeline.ViewSpan * 0.1);
            e.Handled = true;
        }
        else if (e.Delta.Y != 0)
        {
            timeline.ZoomBy(Math.Pow(1.25, e.Delta.Y), Lanes.TimeAt(e.GetPosition(Lanes).X));
            e.Handled = true;
        }
    }

    // Keyboard, while the timeline has focus: Left / Right step through frames, Home / End jump to the first / last frame
    // of the selected aircraft, Up / Down change the selected aircraft.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || ViewModel is not { } viewModel)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Left:
                viewModel.StepPreviousCommand.Execute(null);
                break;
            case Key.Right:
                viewModel.StepNextCommand.Execute(null);
                break;
            case Key.Home:
                viewModel.GoToFirstFrameCommand.Execute(null);
                break;
            case Key.End:
                viewModel.GoToLastFrameCommand.Execute(null);
                break;
            case Key.Up:
                viewModel.SelectNeighbour(-1);
                break;
            case Key.Down:
                viewModel.SelectNeighbour(1);
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
