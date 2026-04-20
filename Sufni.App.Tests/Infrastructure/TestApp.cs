namespace Sufni.App.Tests.Infrastructure;

/// <summary>
/// A <see cref="Sufni.App.App"/> subclass used by the headless test
/// application. The real <c>App</c> overrides
/// <c>OnFrameworkInitializationCompleted</c> to build the entire DI
/// graph (SQLite, HTTP, real coordinators with constructor-time event
/// subscriptions); none of that is wanted in tests, so this subclass
/// short-circuits both initialization steps.
/// </summary>
public sealed class TestApp : Sufni.App.App
{
    public override void Initialize()
    {
        // Skip XAML loading. The real App.axaml pulls in plot/map style
        // includes that the headless test process has no use for.
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Skip the real DI bootstrap.
    }
}
