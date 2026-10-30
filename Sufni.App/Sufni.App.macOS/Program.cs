using Avalonia;
using Avalonia.Native;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using Sufni.App.Desktop;

using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;
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
            var paths = new AppPaths(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sufni.App"));
            LoggingBootstrapper.Initialize("macOS", paths, TimeProvider.System);
            var root = new PlatformRoot(
                new AppCapabilities(
                    CanHostSyncServer: true,
                    CanPairAsClient: false,
                    SupportsMassStorageImport: true,
                    SupportsStorageProviderImport: true,
                    CanPlayVideo: true),
                new InputCapabilities(
                    HasPointer: true,
                    HasTouch: false,
                    HasKeyboard: true,
                    SupportsLongPressContextMenu: false),
                AppPreferences.ReadLaunchValues(paths.PreferencesPath, UiLayoutProfile.Workspace),
                services =>
                {
                    services.AddSingleton<ISecureStorage, MacOsSecureStorage>();
                    services.AddKeyedSingleton<IServiceDiscovery, BonjourServiceDiscovery>("daq");
                    DesktopAppBootstrapper.RegisterDesktopSync(services);
                    services.AddSingleton<IVideoPlayerFactory, MacOsVideoPlayerFactory>();
                },
                paths);
            return DesktopAppBootstrapper.ConfigureAvaloniaApp(
                AppBuilder.Configure(() => new App(root)).UseSkia().UseAvaloniaNative());
        }
    }
}
