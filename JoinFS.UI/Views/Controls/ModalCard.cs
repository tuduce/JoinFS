using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// The shell every overlay shares: a white card with a title row and ✕, a scrolling body, and an optional footer row.
/// Put the body in <see cref="ContentControl.Content"/> and the buttons in <see cref="Footer"/>.
/// The template is in Styles/Controls.axaml.
/// </summary>
public sealed class ModalCard : ContentControl
{
    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<ModalCard, string?>(nameof(Title));

    public static readonly StyledProperty<object?> FooterProperty =
        AvaloniaProperty.Register<ModalCard, object?>(nameof(Footer));

    public static readonly StyledProperty<bool> ShowCloseProperty =
        AvaloniaProperty.Register<ModalCard, bool>(nameof(ShowClose), defaultValue: true);

    public static readonly StyledProperty<ICommand?> CloseCommandProperty =
        AvaloniaProperty.Register<ModalCard, ICommand?>(nameof(CloseCommand));

    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public object? Footer
    {
        get => GetValue(FooterProperty);
        set => SetValue(FooterProperty, value);
    }

    public bool ShowClose
    {
        get => GetValue(ShowCloseProperty);
        set => SetValue(ShowCloseProperty, value);
    }

    public ICommand? CloseCommand
    {
        get => GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }
}
