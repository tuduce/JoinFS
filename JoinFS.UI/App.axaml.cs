using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using JoinFS.UI.ViewModels;
using JoinFS.UI.Views;

namespace JoinFS.UI;

public partial class App : Application
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // The shortcuts ask the system which keys are down, so a key that is tapped has to be caught while it is down: faster than the poll.
    private static readonly TimeSpan ShortcutInterval = TimeSpan.FromMilliseconds(100);

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            UiHost.MainWindowOrNull = window;
            MainViewModel viewModel = new(UiHost.CreateServices());
            window.DataContext = viewModel;
            desktop.MainWindow = window;

            // The live app changes by itself, so the screen looks at it a few times a second.
            DispatcherTimer poll = new() { Interval = PollInterval };
            poll.Tick += (_, _) =>
            {
                try
                {
                    viewModel.Poll();
                }
                catch (Exception ex)
                {
                    // A broken poll must not take the simulator app down with it.
                    UiHost.Log("New UI poll failed: " + ex);
                }
            };
            poll.Start();

            DispatcherTimer shortcuts = new() { Interval = ShortcutInterval };
            shortcuts.Tick += (_, _) =>
            {
                try
                {
                    _ = viewModel.PollShortcuts();
                }
                catch (Exception ex)
                {
                    UiHost.Log("New UI shortcut failed: " + ex);
                }
            };
            shortcuts.Start();
            window.Closed += (_, _) =>
            {
                poll.Stop();
                shortcuts.Stop();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
