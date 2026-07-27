using Avalonia.Headless.XUnit;

namespace Sufni.App.Tests.Infrastructure;

[Collection("Ui")]
public class SmokeTests
{
    [AvaloniaFact]
    public void RealApp_StartupWithoutApplicationLifetime_UsesPreviewInitialization()
    {
        var app = new Sufni.App.App();

        app.OnFrameworkInitializationCompleted();

        Assert.Null(app.Services);
        Assert.Contains(app.DataTemplates, template => template is Sufni.App.ViewLocator);
    }

}
