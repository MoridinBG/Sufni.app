using Avalonia;
using Avalonia.Logging;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.ViewModels;

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
            .With(new SkiaOptions { UseOpacitySaveLayer = true });
    }
}