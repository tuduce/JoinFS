using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;
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

            // Until the real adapters exist, everything runs on the fake services, with the prototype's 900 ms connect.
            // "--skip-onboarding" starts as a returning user, to get past the first-run card while developing.
            // "--xplane" shows what the XPLANE build shows.
            UserSettings? settings = desktop.Args?.Contains("--skip-onboarding") == true
                ? new UserSettings { Onboarded = true, Nickname = "HB-TDX" }
                : null;
            AppServices services = FakeServices.Create(
                TimeSpan.FromMilliseconds(900), settings, platform: new AvaloniaPlatform(() => TopLevel.GetTopLevel(window)),
                xplaneBuild: desktop.Args?.Contains("--xplane") == true);

            window.DataContext = new MainViewModel(services);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
