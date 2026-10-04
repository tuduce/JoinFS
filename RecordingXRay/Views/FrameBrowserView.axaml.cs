using Avalonia.Controls;
using Avalonia.Threading;
using RecordingXRay.ViewModels;

namespace RecordingXRay.Views;

public partial class FrameBrowserView : UserControl
{
    private FrameBrowserViewModel? browser;

    public FrameBrowserView()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (browser is not null)
        {
            browser.RevealRequested -= OnRevealRequested;
        }

        browser = (DataContext as MainViewModel)?.Browser;
        if (browser is not null)
        {
            browser.RevealRequested += OnRevealRequested;
        }
    }

    // The list may not have laid out the new rows yet, so scroll once it has.
    private void OnRevealRequested(FrameRow row) =>
        Dispatcher.UIThread.Post(() => FrameList.ScrollIntoView(row), DispatcherPriority.Loaded);
}
