using Avalonia;
using Avalonia.Headless;
using JoinFS.UI;
using JoinFS.UI.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace JoinFS.UI.Tests;

public static class TestAppBuilder
{
    // Skia with real drawing, so the screenshots (see RenderTests) show what the app renders.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
