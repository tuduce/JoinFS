using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Views;

public partial class HubPicker : UserControl
{
    public static readonly StyledProperty<bool> IsOpenProperty =
        AvaloniaProperty.Register<HubPicker, bool>(nameof(IsOpen));

    public HubPicker()
    {
        InitializeComponent();
        // the entries that have the typed text in their name or address
        Box.ItemFilter = (text, item) => item is AddressBookRow row && Book?.Offers(row, text) == true;
        // picking a suggestion puts the entry's name in the box
        Box.ItemSelector = (_, item) => (item as AddressBookRow)?.Name ?? "";

        // A click opens the whole list (the box only opens it while typing), and focusing selects the text so that typing replaces it.
        Box.AddHandler(PointerPressedEvent, (_, _) => Box.IsDropDownOpen = true, RoutingStrategies.Tunnel);
        Box.GotFocus += (_, e) =>
        {
            if (e.Source is TextBox input)
                Dispatcher.UIThread.Post(input.SelectAll);
        };
        // leaving the box empty puts the pick back, so the Network button always has something to join
        Box.LostFocus += (_, _) => Book?.RestoreText();
    }

    private AddressBookViewModel? Book => (DataContext as MainViewModel)?.AddressBook;

    /// <summary>True while the list of hubs is open, so the host can give the picker room.</summary>
    public bool IsOpen
    {
        get => GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        Box.DropDownOpened += (_, _) => IsOpen = true;
        Box.DropDownClosed += (_, _) => IsOpen = false;
    }
}
