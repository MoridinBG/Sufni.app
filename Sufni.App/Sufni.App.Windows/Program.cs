using Sufni.App.ExtensionHost.Contracts.Services;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;
using System;
using Sufni.App.Desktop;

using Sufni.App.Infrastructure;
namespace Sufni.App.Windows
{
    internal partial class Program
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
            App.ServiceCollection.AddAppEnvironment(
                UiLayoutProfile.Workspace,
                new AppCapabilities(
                    CanHostSyncServer: true,
                    CanPairAsClient: false,
                    SupportsMassStorageImport: true,
                    SupportsStorageProviderImport: true),
                new InputCapabilities(
                    HasPointer: true,
                    HasTouch: false,
                    HasKeyboard: true,
                    SupportsLongPressContextMenu: false));
            App.ServiceCollection.AddSingleton<ISecureStorage, WindowsSecureStorage>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, SocketServiceDiscovery>("daq");
            DesktopAppBootstrapper.RegisterDesktopSync(App.ServiceCollection);
            RegisterPlatformExtensions(App.ServiceCollection);
            return DesktopAppBootstrapper.ConfigureAvaloniaApp(
                AppBuilder.Configure<App>().UsePlatformDetect(),
                "Windows");
        }

        static partial void RegisterPlatformExtensions(IServiceCollection services);
    }
}
