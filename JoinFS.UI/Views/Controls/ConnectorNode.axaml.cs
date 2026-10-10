using Avalonia;
using Avalonia.Controls;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Views.Controls;

public partial class ConnectorNode : UserControl
{
    public static readonly StyledProperty<IconData?> IconProperty =
        AvaloniaProperty.Register<ConnectorNode, IconData?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ConnectorNode, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ButtonTipProperty =
        AvaloniaProperty.Register<ConnectorNode, string?>(nameof(ButtonTip));

    private ConnectionViewModel? _connection;

    public ConnectorNode() => InitializeComponent();

    public IconData? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Tooltip of the round button, e.g. "Connect to simulator".</summary>
    public string? ButtonTip
    {
        get => GetValue(ButtonTipProperty);
        set => SetValue(ButtonTipProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IconProperty)
            NodeIcon.Source = Icon;
        else if (change.Property == TitleProperty)
            TitleText.Text = Title;
        else if (change.Property == ButtonTipProperty)
            UpdateTip();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_connection is not null)
            _connection.PropertyChanged -= OnConnectionChanged;
        _connection = DataContext as ConnectionViewModel;
        if (_connection is not null)
            _connection.PropertyChanged += OnConnectionChanged;
        UpdateTip();
    }

    private void OnConnectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ConnectionViewModel.ShortcutHint) or nameof(ConnectionViewModel.CanCancel))
            UpdateTip();
    }

    /// <summary>
    /// The button's own tip (while an attempt can be given up, what a click does: "Cancel"), and under it the keys of its shortcut
    /// when that is on.
    /// </summary>
    private void UpdateTip()
    {
        string? hint = _connection?.ShortcutHint;
        string? tip = _connection is { CanCancel: true } ? _connection.ActionLabel : ButtonTip;
        ToolTip.SetTip(NodeButton, hint is null ? tip : string.IsNullOrEmpty(tip) ? hint : tip + Environment.NewLine + hint);
        Avalonia.Automation.AutomationProperties.SetName(NodeButton, tip);
    }
}
