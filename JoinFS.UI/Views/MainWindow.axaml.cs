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
    // Collapsed is the design's 376 wide panel, 10 shorter than its 190 (the version shares the row of the link, and the bottom margin is the 16 of the sides); expanded is min(1200, screen - 48) x min(760, screen - 48).
    // With a flight plan loaded the collapsed view has a fourth node (a 16 wire and the 72 node), and the window is that much wider.
    private const double BaseCollapsedWidth = 376, FlightPlanExtraWidth = 88, CollapsedHeight = 180;
    private const double ExpandedMaxWidth = 1200, ExpandedMaxHeight = 760;
    private const double ScreenMargin = 24;
    private const double StartMargin = 4; // the gap to the left and bottom edges of the work area at start
    private static readonly TimeSpan ResizeDuration = TimeSpan.FromMilliseconds(380);

    private readonly DispatcherTimer _resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(8) };
    private readonly Easing _easing = new SplineEasing(0.2, 0.8, 0.2, 1);
    private MainViewModel? _viewModel;

    public MainWindow()
    {
        InitializeComponent();
        _resizeTimer.Tick += (_, _) => StepResize();
        Opened += OnOpened;
        InstallSnapLayouts();

        // Tunnel, so it runs before the focused control: a Button turns any Enter, Alt+Enter included, into a click and would swallow it.
        AddHandler(KeyDownEvent, OnAltEnter, RoutingStrategies.Tunnel);
    }

    // Alt+Enter expands or collapses the window, whatever has the focus, as in the simulators.
    private void OnAltEnter(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.Alt || _viewModel?.ToggleExpandCommand is not { } command)
            return;

        if (command.CanExecute(null))
            command.Execute(null);
        e.Handled = true;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.FlightPlanLoad.PropertyChanged -= OnFlightPlanLoadChanged;
        }
        _viewModel = DataContext as MainViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.FlightPlanLoad.PropertyChanged += OnFlightPlanLoadChanged;
        }
    }

    private double CollapsedWidth =>
        BaseCollapsedWidth + (_viewModel?.FlightPlanLoad.IsConnected == true ? FlightPlanExtraWidth : 0);

    // A flight plan loaded or cleared while collapsed: the window grows or shrinks to the Flight Plan node.
    private void OnFlightPlanLoadChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.IsConnected) && _placed && _viewModel?.IsExpanded == false)
            BeginResize(expanded: false);
    }

    private bool _closeConfirmed;

    // A recording that was not saved is asked about before the window goes, as the old window did. It closes once the user has answered.
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || _viewModel is not { } viewModel || !viewModel.Recorder.HasUnsavedRecording)
            return;

        e.Cancel = true;
        _ = AskThenCloseAsync(viewModel);
    }

    private async Task AskThenCloseAsync(MainViewModel viewModel)
    {
        // the question needs room
        viewModel.IsExpanded = true;
        if (await viewModel.Recorder.AskToSaveAsync())
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // Where the user left the window, or the bottom-left of the screen, close to its edge. The window is placed by its bottom-left
    // corner, so it grows up and to the right. A saved corner on a screen that is gone is dropped.
    private PixelPoint StartAnchor()
    {
        double scale = RenderScaling;
        if (_viewModel?.Profile.WindowAnchor is { } saved)
        {
            PixelPoint inside = new(saved.X, saved.Y - 1);
            foreach (Avalonia.Platform.Screen screen in Screens.All)
            {
                PixelRect area = screen.WorkingArea;
                if (area.Contains(inside))
                {
                    // keep the whole collapsed panel on that screen
                    return new PixelPoint(
                        Math.Min(saved.X, area.Right - (int)(Width * scale)),
                        Math.Max(saved.Y, area.Y + (int)(Height * scale)));
                }
            }
        }

        PixelRect primary = (Screens.ScreenFromWindow(this) ?? Screens.Primary)?.WorkingArea ?? new PixelRect(0, 0, 1920, 1080);
        return new PixelPoint(primary.X + (int)(StartMargin * scale), primary.Bottom - (int)(StartMargin * scale));
    }

    private bool _placed;
    private PixelPoint _lastAnchor;
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // The user moved or resized the window by hand: remember where its bottom-left corner is, once the movement has settled.
    private void NoteMoved()
    {
        if (!_placed || _resizeTimer.IsEnabled || WindowState != WindowState.Normal)
            return;

        double scale = RenderScaling;
        PixelPoint anchor = new(Position.X, Position.Y + (int)Math.Round(Height * scale));
        if (anchor == _lastAnchor)
            return;

        _lastAnchor = anchor;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SavePlacement()
    {
        _saveTimer.Stop();
        _viewModel?.Profile.SaveWindowAnchor(_lastAnchor.X, _lastAnchor.Y);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (_viewModel?.IsExpanded != true)
            Width = CollapsedWidth;
        PixelPoint anchor = StartAnchor();
        Position = new PixelPoint(anchor.X, anchor.Y - (int)(Height * RenderScaling));
        _lastAnchor = anchor;
        _placed = true;
        _saveTimer.Tick += (_, _) => SavePlacement();
        PositionChanged += (_, _) => NoteMoved();
        SizeChanged += (_, _) => NoteMoved();
        Closed += (_, _) =>
        {
            if (_saveTimer.IsEnabled)
                SavePlacement();
        };

        // A first run starts expanded, for the welcome card; the window grows from its corner like any expand.
        if (_viewModel?.IsExpanded == true)
            BeginResize(expanded: true);

        // a recording that was never saved is offered back
        if (_viewModel is { } viewModel)
            _ = viewModel.Recorder.OfferRecoveryAsync();
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
        _anchor = new PixelPoint(Position.X, Position.Y + (int)Math.Round(Height * scale));

        // The full view has to fit on the screen, so a window left near the top or the right edge is moved down or left as far as needed.
        // The same for the collapsed window when it widens for the flight plan.
        _anchor = new PixelPoint(Math.Min(_anchor.X, area.Right - (int)Math.Round(_toWidth * scale)), _anchor.Y);
        if (expanded)
            _anchor = new PixelPoint(_anchor.X, Math.Max(_anchor.Y, area.Y + (int)Math.Round(_toHeight * scale)));

        // Resizing by hand is only for the full view. Its minimum size would clamp a collapse partway,
        // so it is lifted for the animation and restored at the end.
        CanResize = false;
        MinWidth = 0;
        MinHeight = 0;
        _resizeStart = DateTime.UtcNow;
        _resizeTimer.Start();
    }

    private void StepResize()
    {
        double t = Math.Min(1, (DateTime.UtcNow - _resizeStart) / ResizeDuration);
        double eased = _easing.Ease(t);

        Width = _fromWidth + (_toWidth - _fromWidth) * eased;
        Height = _fromHeight + (_toHeight - _fromHeight) * eased;
        Position = new PixelPoint(_anchor.X, _anchor.Y - (int)Math.Round(Height * RenderScaling));

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
