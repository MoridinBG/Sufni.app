using System;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Harness;

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
    public static void SetIsDesktop(bool isDesktop)
    {
        var app = Sufni.App.App.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        app.SetIsDesktopForTests(isDesktop);
    }

    public static IDisposable UsePointerInput()
    {
        return UseInputCapabilities(new InputCapabilities(
            HasPointer: true,
            HasTouch: false,
            HasKeyboard: true,
            SupportsPinch: false,
            SupportsLongPressContextMenu: false));
    }

    public static IDisposable UseTouchInput()
    {
        return UseInputCapabilities(new InputCapabilities(
            HasPointer: false,
            HasTouch: true,
            HasKeyboard: false,
            SupportsPinch: true,
            SupportsLongPressContextMenu: true));
    }

    public static IDisposable UseInputCapabilities(InputCapabilities input)
    {
        var profile = input.HasTouch && !input.HasPointer
            ? UiLayoutProfile.Compact
            : UiLayoutProfile.Workspace;
        var environment = new AppEnvironment(
            profile,
            profile,
            new AppCapabilities(
                CanHostSyncServer: input.HasPointer,
                CanPairAsClient: input.HasTouch,
                HasHaptics: input.HasTouch,
                SupportsMassStorageImport: input.HasPointer,
                SupportsStorageProviderImport: input.HasTouch,
                SupportsNativeWindowing: input.HasPointer),
            input);
        return UseAppEnvironment(environment);
    }

    public static IDisposable UseAppEnvironment(IAppEnvironment environment)
    {
        var app = Sufni.App.App.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var previousServices = app.Services;
        var services = new ServiceCollection()
            .AddSingleton(environment)
            .BuildServiceProvider();
        app.SetServicesForTests(services);
        return new TestServiceScope(app, services, previousServices);
    }

    public override void Initialize()
    {
        // Skip XAML loading. The real App.axaml pulls in plot/map style
        // includes that the headless test process has no use for.
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Skip the real DI bootstrap.
    }

    private sealed class TestServiceScope(
        Sufni.App.App app,
        ServiceProvider services,
        IServiceProvider? previousServices) : IDisposable
    {
        public void Dispose()
        {
            app.SetServicesForTests(previousServices);
            services.Dispose();
        }
    }
}
