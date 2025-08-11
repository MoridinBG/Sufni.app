using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using System;
using ServiceDiscovery;
using Sufni.App.Services;

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
            RegisteredServices.Collection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            RegisteredServices.Collection.AddSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>();
            RegisteredServices.Collection.AddSingleton<ISynchronizationServerService, SynchronizationServerService>();
            return AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}
