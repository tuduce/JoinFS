using Avalonia;
using Avalonia.Controls;

namespace JoinFS.UI.Views;

public partial class HubPicker : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<HubPicker, bool>(nameof(IsOpen));

    public HubPicker() => InitializeComponent();

    /// <summary>True while the list of hubs is open, so the host can give the picker room.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    protected override void OnLoaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Combo.DropDownOpened += (_, _) => IsOpen = true;
        Combo.DropDownClosed += (_, _) => IsOpen = false;
    }
}
