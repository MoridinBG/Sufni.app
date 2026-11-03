using Sufni.App.Shell.Services.Preferences;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Sufni.App.Infrastructure;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Infrastructure;

// Application initialization loads Avalonia resources.
// Loads the application XAML and Avalonia resources.
[Collection("Ui")]
public class SmokeTests
{
    [AvaloniaFact]
    public async Task RealApp_Initialize_AppliesLaunchThemeBeforeLoadingResources()
    {
        using var directory = new TempDirectory("sufni-launch-theme-test");
        var path = Path.Combine(directory.Path, "app-preferences.json");
        await File.WriteAllTextAsync(path, """{"theme":{"mode":"Light"}}""",
            TestContext.Current.CancellationToken);
        var launch = AppPreferences.ReadLaunchValues(path, UiLayoutProfile.Workspace);
        var app = new App(TestAppEnvironment.CreateRoot(launch));

        app.Initialize();

        Assert.Equal(ThemeVariant.Light, app.RequestedThemeVariant);
    }
}
