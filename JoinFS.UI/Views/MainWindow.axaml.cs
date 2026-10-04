using System.ComponentModel;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Views;

public partial class MainWindow : Window
{
    // Collapsed is the design's 376 x 190 panel; expanded is min(1200, screen - 48) x min(760, screen - 48).
    private const double CollapsedWidth = 376, CollapsedHeight = 190;
    private const double ExpandedMaxWidth = 1200, ExpandedMaxHeight = 760;
    private const double ScreenMargin = 24;
    private static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(380);

    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private readonly Easing _easing = new SplineEasing(0.2, 0.8, 0.2, 1);
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _resizeTimer.Tick += (_, _) => StepResize();
        Opened += OnOpened;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Like the design, start in the bottom-left of the screen.
        PixelRect area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        double scale = RenderScaling;
        Position = new PixelPoint(
            area.X + (int)(ScreenMargin * scale),
            area.Bottom - (int)((Height + ScreenMargin) * scale));
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsExpanded) && _viewModel is not null)
            BeginResize(_viewModel.IsExpanded);
    }

    // ---- Animated resize. The bottom-left corner stays put, so the panel grows up and to the right. ----

    private double _fromWidth, _fromHeight, _toWidth, _toHeight;
    private PixelPoint _anchor; // bottom-left corner, in pixels
    private DateTime _resizeStart;

    private void BeginResize(bool expanded)
    {
        if (WindowState != WindowState.Normal)
            WindowState = WindowState.Normal;

        PixelRect area = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        double scale = RenderScaling;

        _fromWidth = Width;
        _fromHeight = Height;
        _toWidth = expanded ? Math.Min(ExpandedMaxWidth, area.Width / scale - 2 * ScreenMargin) : CollapsedWidth;
        _toHeight = expanded ? Math.Min(ExpandedMaxHeight, area.Height / scale - 2 * ScreenMargin) : CollapsedHeight;
        _anchor = new PixelPoint(Position.X, Position.Y + (int)(Height * scale));

        // Resizing by hand is only for the full view.
        CanResize = false;
        _resizeStart = DateTime.UtcNow;
        _resizeTimer.Start();
    }

    private void StepResize()
    {
        double t = Math.Min(1, (DateTime.UtcNow - _resizeStart) / ResizeDuration);
        double eased = _easing.Ease(t);

        Width = _fromWidth + (_toWidth - _fromWidth) * eased;
        Height = _fromHeight + (_toHeight - _fromHeight) * eased;
        Position = new PixelPoint(_anchor.X, _anchor.Y - (int)(Height * RenderScaling));

        if (t >= 1)
        {
            _resizeTimer.Stop();
            CanResize = _viewModel?.IsExpanded == true;
            MinWidth = _viewModel?.IsExpanded == true ? 900 : 0;
            MinHeight = _viewModel?.IsExpanded == true ? 560 : 0;
        }
    }

    // ---- Title bar ----

    private void OnTitleBarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source is not Button && !IsInsideButton(e.Source))
            BeginMoveDrag(e);
    }

    private static bool IsInsideButton(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<Button>() is not null;

    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_viewModel?.IsExpanded == true && !IsInsideButton(e.Source))
            ToggleMaximize();
    }

    private void OnMinimizeClick(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object? sender, RoutedEventArgs e) => ToggleMaximize();

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    // ---- Overlay ----

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Escape closes the overlay, except the first-run card, which has to be completed.
        if (e.Key == Key.Escape && _viewModel?.Overlay is { IsDismissable: true } overlay)
        {
            overlay.Close();
            e.Handled = true;
        }
    }
}
