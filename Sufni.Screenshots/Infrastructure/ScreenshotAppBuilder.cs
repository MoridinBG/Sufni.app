using Avalonia;
using Avalonia.Headless;
using Avalonia.Skia;
using Sufni.Screenshots.Infrastructure;

[assembly: AvaloniaTestApplication(typeof(ScreenshotAppBuilder))]

namespace Sufni.Screenshots.Infrastructure;

public static class ScreenshotAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<ScreenshotApp>()
        .UseSkia()
        .UseHarfBuzz()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions
        {
            UseHeadlessDrawing = false,
        });
}
