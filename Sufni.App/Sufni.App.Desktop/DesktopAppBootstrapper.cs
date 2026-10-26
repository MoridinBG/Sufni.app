using Avalonia;
using Avalonia.Logging;
using Avalonia.Skia;
using Microsoft.Extensions.DependencyInjection;

using Sufni.App.Infrastructure;
using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.SyncAndPairing.ViewModels;
namespace Sufni.App.Desktop;

public static class DesktopAppBootstrapper
{
    public static void RegisterDesktopSync(IServiceCollection services)
    {
        services.AddSingleton<ISynchronizationServerService, SynchronizationServerService>();
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
