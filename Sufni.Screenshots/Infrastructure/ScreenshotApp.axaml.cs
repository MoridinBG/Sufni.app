using Avalonia.Styling;

namespace Sufni.Screenshots.Infrastructure;

public class ScreenshotApp : Sufni.App.App
{
#if DEBUG
    protected override bool ShouldAttachDeveloperTools => false;
#endif

    public override void Initialize()
    {
        base.Initialize();
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // No DI setup — this app exists only for screenshot capture.
    }
}
