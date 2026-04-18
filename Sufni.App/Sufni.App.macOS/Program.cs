using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using Sufni.App.Desktop;
using Sufni.App.Services;

namespace Sufni.App.macOS
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
            App.ServiceCollection.AddSingleton<ISecureStorage, MacOsSecureStorage>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, BonjourServiceDiscovery>("gosst");
            DesktopAppBootstrapper.RegisterDesktopSync(App.ServiceCollection);
            return DesktopAppBootstrapper.ConfigureAvaloniaApp(
                AppBuilder.Configure<App>().UsePlatformDetect(),
                "macOS");
        }
    }
}
