using Avalonia.Controls;
using Avalonia.Threading;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Views.Tabs;

public partial class MonitorView : UserControl
{
    public MonitorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow();
    }

    // The log is read from its end: new lines scroll into view as they come, as in the old window.
    private void Follow()
    {
        if (DataContext is not MonitorViewModel monitor)
            return;

        monitor.LogLines.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(() => LogScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
}
