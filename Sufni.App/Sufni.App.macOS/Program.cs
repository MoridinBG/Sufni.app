using Sufni.App.ExtensionHost.Contracts.Services;
using Avalonia;
using Avalonia.Native;
using Microsoft.Extensions.DependencyInjection;
using System;
using Sufni.App.Desktop;

using Sufni.App.Infrastructure;
namespace Sufni.App.macOS
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
                    HasHaptics: false,
                    SupportsMassStorageImport: true,
                    SupportsStorageProviderImport: true,
                    SupportsNativeWindowing: true),
                new InputCapabilities(
                    HasPointer: true,
                    HasTouch: false,
                    HasKeyboard: true,
                    SupportsPinch: false,
                    SupportsLongPressContextMenu: false));
            App.ServiceCollection.AddSingleton<ISecureStorage, MacOsSecureStorage>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, BonjourServiceDiscovery>("daq");
            DesktopAppBootstrapper.RegisterDesktopSync(App.ServiceCollection);
            RegisterPlatformExtensions(App.ServiceCollection);
            return DesktopAppBootstrapper.ConfigureAvaloniaApp(
                AppBuilder.Configure<App>().UseSkia().UseAvaloniaNative(),
                "macOS");
        }

        static partial void RegisterPlatformExtensions(IServiceCollection services);
    }
}
