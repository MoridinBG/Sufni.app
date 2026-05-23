using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Infrastructure;

public class SmokeTests
{
    [AvaloniaFact]
    public void HeadlessApp_Initializes_AndAppCurrent_IsTestApp()
    {
        Assert.NotNull(Sufni.App.App.Current);
        Assert.IsType<TestApp>(Sufni.App.App.Current);
    }

    [AvaloniaFact]
    public void RealApp_StartupWithoutApplicationLifetime_UsesPreviewInitialization()
    {
        var app = new Sufni.App.App();

        app.OnFrameworkInitializationCompleted();

        Assert.Null(app.Services);
        Assert.Contains(app.DataTemplates, template => template is Sufni.App.ViewLocator);
    }

}
