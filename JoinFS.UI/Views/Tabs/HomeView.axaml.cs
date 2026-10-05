using Avalonia.Controls;
using Avalonia.Interactivity;

namespace JoinFS.UI.Views.Tabs;

public partial class HomeView : UserControl
{
    public HomeView() => InitializeComponent();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Map.ZoomIn();

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Map.ZoomOut();

    private void OnFit(object? sender, RoutedEventArgs e) => Map.FitToMarkers();
}
