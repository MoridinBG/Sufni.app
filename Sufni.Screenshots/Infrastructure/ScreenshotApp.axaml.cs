using Sufni.App.Tests.TestSupport.Harness;
using Avalonia.Styling;

namespace Sufni.Screenshots.Infrastructure;

public class ScreenshotApp : Sufni.App.App
{
    public ScreenshotApp() : base(TestAppEnvironment.CreateRoot()) { }


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
