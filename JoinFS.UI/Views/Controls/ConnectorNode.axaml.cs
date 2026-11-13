using System.Windows.Input;
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

    public static readonly StyledProperty<string?> StateTextProperty =
        AvaloniaProperty.Register<ConnectorNode, string?>(nameof(StateText));

    public static readonly StyledProperty<ICommand?> ClickCommandProperty =
        AvaloniaProperty.Register<ConnectorNode, ICommand?>(nameof(ClickCommand));

    private ConnectionViewModel? _connection;

    public ConnectorNode() => InitializeComponent();

    /// <summary>What a click does in place of the connection's own toggle, when it is set.</summary>
    public ICommand? ClickCommand
    {
        get => GetValue(ClickCommandProperty);
        set => SetValue(ClickCommandProperty, value);
    }

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

    /// <summary>Shown in the second row in place of the state label while it is not empty.</summary>
    public string? StateText
    {
        get => GetValue(StateTextProperty);
        set => SetValue(StateTextProperty, value);
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
        else if (change.Property == ClickCommandProperty)
            UpdateCommand();
        else if (change.Property == StateTextProperty)
        {
            bool set = !string.IsNullOrEmpty(StateText);
            StateTextBlock.Text = StateText;
            StateTextBlock.IsVisible = set;
            StateLabelText.IsVisible = !set;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_connection is not null)
            _connection.PropertyChanged -= OnConnectionChanged;
        _connection = DataContext as ConnectionViewModel;
        if (_connection is not null)
            _connection.PropertyChanged += OnConnectionChanged;
        UpdateCommand();
        UpdateTip();
    }

    // A click toggles the connection, unless the node was given something else to do.
    private void UpdateCommand() => NodeButton.Command = ClickCommand ?? _connection?.ToggleCommand;

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
