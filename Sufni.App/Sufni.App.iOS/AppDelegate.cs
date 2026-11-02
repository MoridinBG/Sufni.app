using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.iOS;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Infrastructure;
using Sufni.App.Video.Models;
using UIKit;

namespace Sufni.App.iOS
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the 
    // User Interface of the application, as well as listening (and optionally responding) to 
    // application events from iOS.
    [Register("AppDelegate")]
    public partial class AppDelegate : AvaloniaAppDelegate<Avalonia.Application>
    {
        private NSObject? didEnterBackgroundObserver;
        private NSObject? willTerminateObserver;

        protected override AppBuilder CreateAppBuilder()
        {
            var paths = new AppPaths(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sufni.App"));
            LoggingBootstrapper.Initialize("iOS", paths, TimeProvider.System, new OsLogSink(LoggingBootstrapper.OutputTemplate));
            InstallLifecycleObservers();
            var root = new PlatformRoot(
                new AppCapabilities(
                    CanHostSyncServer: false,
                    CanPairAsClient: true,
                    SupportsMassStorageImport: false,
                    SupportsStorageProviderImport: true,
                    CanPickFolder: true,
                    CanPlayVideo: true),
                new InputCapabilities(
                    HasPointer: false,
                    HasTouch: true,
                    HasKeyboard: false,
                    SupportsLongPressContextMenu: true,
                    CommandModifier: CommandModifier.Meta),
                AppPreferences.ReadLaunchValues(paths.PreferencesPath, UiLayoutProfile.Compact),
                services =>
                {
                    MobileAppBootstrapper.RegisterMobileSync(
                        services,
                        static () => new IosSecureStorage(),
                        static () => new IosFriendlyNameProvider(),
                        static role => new BonjourServiceDiscovery(role == "daq"
                            ? ServiceDiscoveryResolution.AnnouncedAddresses
                            : ServiceDiscoveryResolution.ReachableTcpEndpoint),
                        static () => new IosHapticFeedback());
                    services.AddSingleton<IVideoPlayerFactory, IosVideoPlayerFactory>();
                },
                paths);

            return MobileAppBootstrapper.ConfigureMobileAvalonia(
                AppBuilder.Configure(() => new App(root)).UseiOS(this));
        }

        private static async Task OnBackgroundAsync()
        {
            var application = UIApplication.SharedApplication;
            var taskId = UIApplication.BackgroundTaskInvalid;
            taskId = application.BeginBackgroundTask(() =>
            {
                if (taskId == UIApplication.BackgroundTaskInvalid) return;
                application.EndBackgroundTask(taskId);
                taskId = UIApplication.BackgroundTaskInvalid;
            });
            try
            {
                if (global::Sufni.App.App.Current is { } app) await app.OnBackgroundAsync();
            }
            finally
            {
                if (taskId != UIApplication.BackgroundTaskInvalid) application.EndBackgroundTask(taskId);
            }
        }

        private void InstallLifecycleObservers()
        {
            didEnterBackgroundObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                UIApplication.DidEnterBackgroundNotification,
                async _ => await OnBackgroundAsync());

            willTerminateObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                UIApplication.WillTerminateNotification,
                _ => LoggingBootstrapper.FlushAndClose());
        }
    }
}
