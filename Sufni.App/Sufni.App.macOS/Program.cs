using Sufni.App.ExtensionHost.Contracts.Services;
using Avalonia;
using Avalonia.Native;
using Microsoft.Extensions.DependencyInjection;
using System;
using Sufni.App.Desktop;
#if SUFNI_PROFILING_DIAGNOSTICS
using Sufni.Profiling;
#endif

using Sufni.App.Infrastructure;
namespace Sufni.App.macOS
{
    internal partial class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
#if SUFNI_PROFILING_DIAGNOSTICS
            args = ProfilingRuntime.Initialize(args);
            if (ProfilingRuntime.Options.AppDataPath is { } appDataPath)
            {
                AppPaths.UseProfilingAppDataDirectory(appDataPath);
            }

            try
            {
                StartApp(args);
            }
            finally
            {
                ProfilingRuntime.Shutdown();
            }
#else
            StartApp(args);
#endif
        }

        private static void StartApp(string[] args) => BuildAvaloniaApp()
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
