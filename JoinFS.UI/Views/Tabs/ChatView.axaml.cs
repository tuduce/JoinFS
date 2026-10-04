using Avalonia.Controls;
using Avalonia.Threading;
using JoinFS.UI.ViewModels.Tabs;

namespace JoinFS.UI.Views.Tabs;

public partial class ChatView : UserControl
{
    public ChatView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow();
    }

    // The newest line is the one to read: new lines scroll into view as they come.
    private void Follow()
    {
        if (DataContext is not ChatViewModel chat)
            return;

        chat.Messages.CollectionChanged += (_, _) => Dispatcher.UIThread.Post(() => MessageScroll.ScrollToEnd(), DispatcherPriority.Loaded);
        Dispatcher.UIThread.Post(() => MessageScroll.ScrollToEnd(), DispatcherPriority.Loaded);
    }
}
