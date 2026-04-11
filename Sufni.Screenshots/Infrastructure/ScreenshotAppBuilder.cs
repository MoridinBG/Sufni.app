using Avalonia;
using Avalonia.Headless;
using Sufni.Screenshots.Infrastructure;

[assembly: AvaloniaTestApplication(typeof(ScreenshotAppBuilder))]

namespace Sufni.Screenshots.Infrastructure;

public static class ScreenshotAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<ScreenshotApp>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false,
        });
}
