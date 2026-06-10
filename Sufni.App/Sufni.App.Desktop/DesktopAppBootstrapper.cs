using Avalonia;
using Avalonia.Logging;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Sync;
using Sufni.App.Services;
using Sufni.App.ViewModels;
using Sufni.App.ExtensionHost.Services;

namespace Sufni.App.Desktop;

public static class DesktopAppBootstrapper
{
    public static void RegisterDesktopSync(IServiceCollection services)
    {
        services.AddSingleton<ISynchronizationServerService>(sp => new SynchronizationServerService(
            sp.GetRequiredService<IDatabaseService>(),
            sp.GetRequiredService<IAppPreferences>(),
            sp.GetRequiredService<ISecureStorage>(),
            sp.GetService<IExtensionSyncService>()));
        services.AddSingleton<IPairingServerCoordinator, PairingServerCoordinator>();
        services.AddSingleton<IInboundSyncCoordinator, InboundSyncCoordinator>();
        services.AddSingleton<PairingServerViewModel>();
    }

    public static AppBuilder ConfigureAvaloniaApp(AppBuilder builder, string platformName)
    {
        LoggingBootstrapper.Initialize(platformName);
        Logger.Sink = new AvaloniaSerilogSink(LogEventLevel.Warning);

        return builder
            .WithInterFont()
            .With(new SkiaOptions { UseOpacitySaveLayer = true });
    }
}
