using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Views.Tabs;

public partial class HubsView : UserControl
{
    public HubsView()
    {
        InitializeComponent();
        // Tunnel: the second press of a double-click is seen before the row button can turn it into a click.
        AddHandler(PointerPressedEvent, OnPointerPressed, RoutingStrategies.Tunnel);
    }

    // The first click of a double-click toggles the row as any click does. The second is taken here: it joins the hub and,
    // being handled, never reaches the button, so the accordion is not toggled back.
    private async void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.ClickCount != 2 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;
        if (e.Source is not Visual source || source.FindAncestorOfType<Button>(includeSelf: true) is not { } button
            || !button.Classes.Contains("rowButton") || button.DataContext is not HubRowViewModel row)
            return;

        e.Handled = true;
        await row.JoinOnDoubleClickAsync();
    }
}
