using Avalonia;
using Avalonia.Markup.Xaml;

namespace Sufni.Screenshots.Infrastructure;

public class ScreenshotApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // No DI setup — this app exists only for screenshot capture.
    }
}
