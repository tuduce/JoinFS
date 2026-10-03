using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views;

public partial class MapView : UserControl
{
    public MapView()
    {
        InitializeComponent();
        AttributionLink.PointerPressed += OnAttributionPressed;
    }

    // The credit links to the OpenStreetMap copyright page.
    private void OnAttributionPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is MapViewModel { AttributionUrl.Length: > 0 } map)
        {
            try
            {
                Process.Start(new ProcessStartInfo(map.AttributionUrl) { UseShellExecute = true });
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                // No browser to open: the credit is still shown.
            }

            e.Handled = true;
        }
    }
}
