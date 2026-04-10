using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using ServiceDiscovery;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.ViewModels;

namespace Sufni.App.Linux
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args) => BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
        {
            App.ServiceCollection.AddSingleton<ISecureStorage, LinuxSecureStorage>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("gosst");
            App.ServiceCollection.AddSingleton<ISynchronizationServerService, SynchronizationServerService>();
            App.ServiceCollection.AddSingleton<IPairingServerCoordinator, PairingServerCoordinator>();
            App.ServiceCollection.AddSingleton<IInboundSyncCoordinator, InboundSyncCoordinator>();
            App.ServiceCollection.AddSingleton<PairingServerViewModel>();
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}
