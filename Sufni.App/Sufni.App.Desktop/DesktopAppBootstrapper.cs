using Avalonia;
using Avalonia.Logging;
using Avalonia.Skia;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.Sync;
using Sufni.App.Services;
using Sufni.App.ViewModels;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHosting.Sync;

namespace Sufni.App.Desktop;

public static class DesktopAppBootstrapper
{
    public static void RegisterDesktopSync(IServiceCollection services)
    {
        services.AddSingleton<ISynchronizationServerService>(sp => new SynchronizationServerService(
            sp.GetRequiredService<ISyncDataStore>(),
            sp.GetRequiredService<IPairedDeviceRepository>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<IAppPreferences>(),
            sp.GetRequiredService<ISecureStorage>(),
            sp.GetRequiredService<ISessionBlobSwapRequestStore>(),
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
            .UseHarfBuzz()
            .With(new SkiaOptions { UseOpacitySaveLayer = true });
    }
}
