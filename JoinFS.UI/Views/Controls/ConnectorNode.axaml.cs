using Avalonia;
using Avalonia.Controls;

namespace JoinFS.UI.Views.Controls;

public partial class ConnectorNode : UserControl
{
    public static readonly StyledProperty<IconData?> IconProperty =
        AvaloniaProperty.Register<ConnectorNode, IconData?>(nameof(Icon));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ConnectorNode, string?>(nameof(Title));

    public static readonly StyledProperty<string?> ButtonTipProperty =
        AvaloniaProperty.Register<ConnectorNode, string?>(nameof(ButtonTip));

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
            ToolTip.SetTip(NodeButton, ButtonTip);
    }
}
