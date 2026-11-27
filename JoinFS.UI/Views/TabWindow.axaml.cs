using Avalonia.Controls;
using Avalonia.Input;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Views;

/// <summary>A tab in a window of its own. The main window opens and closes it, following <see cref="MainViewModel.TabWindows"/>.</summary>
public partial class TabWindow : Window
{
    private TabWindowViewModel? _viewModel;

    public TabWindow() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.ActivateRequested -= OnActivateRequested;
        _viewModel = DataContext as TabWindowViewModel;
        if (_viewModel is not null)
            _viewModel.ActivateRequested += OnActivateRequested;
    }

    private void OnActivateRequested(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    // However it was closed (its own ✕, Put back, the main window closing), the tab goes back into the main window.
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_viewModel is { } viewModel)
        {
            viewModel.ActivateRequested -= OnActivateRequested;
            viewModel.OnClosed();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Escape closes the card over this window, as in the main window.
        if (e.Key == Key.Escape && _viewModel?.Overlay is { IsDismissable: true } overlay)
        {
            overlay.Close();
            e.Handled = true;
        }
    }
}
