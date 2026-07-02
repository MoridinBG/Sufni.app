using System;
using Avalonia;
using Avalonia.Logging;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Infrastructure;
using Sufni.App.Extensibility.Sync;
using Sufni.App.Sessions.Processing.Services;
using Sufni.App.Sessions.Services;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.ViewModels;
namespace Sufni.App;

public static class MobileAppBootstrapper
{
    public static void RegisterMobileSync(
        IServiceCollection services,
        Func<ISecureStorage> createSecureStorage,
        Func<IFriendlyNameProvider> createFriendlyNameProvider,
        Func<string, IServiceDiscovery> createServiceDiscovery,
        Func<IHapticFeedback> createHapticFeedback)
    {
        services.AddSingleton(_ => createSecureStorage());
        services.AddSingleton(_ => createFriendlyNameProvider());
        services.AddKeyedSingleton<IServiceDiscovery>("daq", (_, key) => createServiceDiscovery((string)key!));
        services.AddKeyedSingleton<IServiceDiscovery>("sync", (_, key) => createServiceDiscovery((string)key!));
        services.AddSingleton(_ => createHapticFeedback());
        services.AddSingleton<ISynchronizationClientService>(sp => new SynchronizationClientService(
            sp.GetRequiredService<ISyncDataStore>(),
            sp.GetRequiredService<ISessionRepository>(),
            sp.GetRequiredService<ISessionTelemetryWriter>(),
            sp.GetRequiredService<IRecordedSessionSourceRepository>(),
            sp.GetRequiredService<IHttpApiService>(),
            sp.GetRequiredService<IAppPreferences>(),
            sp.GetService<IExtensionSyncService>()));
        services.AddSingleton<IPairingClientCoordinator, PairingClientCoordinator>();
        services.AddSingleton<PairingClientViewModel>();
    }

    public static AppBuilder ConfigureMobileAvalonia(AppBuilder builder)
    {
        Logger.Sink = new AvaloniaSerilogSink(LogEventLevel.Warning);

        return builder
            .WithInterFont()
            .With(new SkiaOptions { UseOpacitySaveLayer = true });
    }
}
