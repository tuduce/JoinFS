using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using JoinFS.UI.Localization;
using JoinFS.UI.Models;
using JoinFS.UI.ViewModels;

namespace JoinFS.UI.Views.Controls;

/// <summary>
/// The button at the top right of a tab that can live in a window of its own. In the main window it opens the tab in its own window;
/// in that window it puts the tab back. It asks the window it is in which of the two it is, so a tab's view needs no binding to the shell.
/// </summary>
public sealed class UndockButton : Button
{
    public static readonly StyledProperty<TabId> TabProperty = AvaloniaProperty.Register<UndockButton, TabId>(nameof(Tab));

    private readonly Icon _icon = new() { Width = 14, Height = 14 };

    public UndockButton()
    {
        Classes.Add("ghost");
        Content = _icon;
    }

    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>The tab this button undocks.</summary>
    public TabId Tab
    {
        get => GetValue(TabProperty);
        set => SetValue(TabProperty, value);
    }

    private bool InTabWindow => TopLevel.GetTopLevel(this)?.DataContext is TabWindowViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);

        bool docked = !InTabWindow;
        _icon.Source = this.FindResource(docked ? "IconUndock" : "IconDock") as IconData;
        string tip = docked ? Loc.T("Open in a separate window") : Loc.T("Put back in the main window");
        ToolTip.SetTip(this, tip);
        Avalonia.Automation.AutomationProperties.SetName(this, tip);
    }

    protected override void OnClick()
    {
        base.OnClick();

        switch (TopLevel.GetTopLevel(this)?.DataContext)
        {
            case TabWindowViewModel window:
                window.DockCommand.Execute(null);
                break;
            case MainViewModel main:
                main.Undock(Tab);
                break;
        }
    }
}
