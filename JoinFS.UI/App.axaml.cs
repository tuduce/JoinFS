using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JoinFS.UI.ViewModels;
using JoinFS.UI.Views;

namespace JoinFS.UI;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new();
            UiHost.MainWindowOrNull = window;
            window.DataContext = new MainViewModel(UiHost.CreateServices());
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
