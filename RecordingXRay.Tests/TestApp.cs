using Avalonia;
using Avalonia.Headless;
using RecordingXRay;
using RecordingXRay.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace RecordingXRay.Tests;

public static class TestAppBuilder
{
    // Skia with real drawing, so screenshots (see ScreenshotTests) show what the app renders.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
