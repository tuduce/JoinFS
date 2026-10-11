using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using JoinFS.UI.Localization;
using JoinFS.UI.Services;
using JoinFS.UI.Services.Fake;

namespace JoinFS.UI;

/// <summary>
/// How a program starts the UI. JoinFS passes services built on the live app; the dev launcher passes the fakes.
/// Run blocks on the calling thread until the window is closed, so call it from the thread that should own the UI.
/// </summary>
public static class UiHost
{
    private static Func<IPlatform, AppServices>? _servicesFactory;
    private static string[] _args = [];
    private static Action<string>? _log;

    /// <summary>The window's platform services (clipboard, file pickers, browser). Give it to the adapters that need one.</summary>
    public static IPlatform Platform { get; } = new AvaloniaPlatform(() => MainWindowOrNull);

    internal static Window? MainWindowOrNull { get; set; }

    internal static string[] Args => _args;

    /// <summary>Something went wrong that the UI itself cannot show. Goes to the host's log, or nowhere.</summary>
    internal static void Log(string message) => _log?.Invoke(message);

    internal static AppServices CreateServices()
    {
        if (_servicesFactory is not null)
            return _servicesFactory(Platform);

        // No host: the fakes, with the prototype's 900 ms connect.
        // "--skip-onboarding" starts as a returning user; "--xplane" shows what the XPLANE build shows.
        UserSettings? settings = _args.Contains("--skip-onboarding") ? new UserSettings { Onboarded = true, Nickname = "HB-TDX" } : null;
        return FakeServices.Create(TimeSpan.FromMilliseconds(900), settings, Platform, xplaneBuild: _args.Contains("--xplane"));
    }

    /// <param name="servicesFactory">Builds the services, given the window's platform services.</param>
    /// <param name="log">Where to report a failure the UI cannot show, such as a poll that threw.</param>
    public static void Run(Func<IPlatform, AppServices> servicesFactory, string[] args, Action<string>? log = null)
    {
        _servicesFactory = servicesFactory;
        _log = log;
        Start(args);
    }

    public static void RunOnFakeServices(string[] args)
    {
        _servicesFactory = null;
        Start(args);
    }

    private static void Start(string[] args)
    {
        _args = args;

        // "--lang de" shows the UI in another language than the system's (de es fr it ko nl pt ru; anything else is English).
        int lang = Array.IndexOf(args, "--lang");
        if (lang >= 0 && lang + 1 < args.Length)
        {
            try
            {
                Loc.Culture = CultureInfo.GetCultureInfo(args[lang + 1]);
            }
            catch (CultureNotFoundException)
            {
                Log("Unknown language: " + args[lang + 1]);
            }
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Also used by the previewer and the headless tests.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
