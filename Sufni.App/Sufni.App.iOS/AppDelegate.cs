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
    public partial class AppDelegate : AvaloniaAppDelegate<App>
    {
        private NSObject? didEnterBackgroundObserver;
        private NSObject? willTerminateObserver;

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            LoggingBootstrapper.Initialize("iOS", new OsLogSink(LoggingBootstrapper.OutputTemplate));
            InstallLifecycleObservers();
            App.ServiceCollection.AddAppEnvironment(
                UiLayoutProfile.Compact,
                new AppCapabilities(
                    CanHostSyncServer: false,
                    CanPairAsClient: true,
                    SupportsMassStorageImport: false,
                    SupportsStorageProviderImport: true,
                    CanPlayVideo: true),
                new InputCapabilities(
                    HasPointer: false,
                    HasTouch: true,
                    HasKeyboard: false,
                    SupportsLongPressContextMenu: true));
            MobileAppBootstrapper.RegisterMobileSync(
                App.ServiceCollection,
                static () => new IosSecureStorage(),
                static () => new IosFriendlyNameProvider(),
                static _ => new BonjourServiceDiscovery(),
                static () => new IosHapticFeedback());
            App.ServiceCollection.AddSingleton<IVideoPlayerFactory, IosVideoPlayerFactory>();

            return MobileAppBootstrapper.ConfigureMobileAvalonia(
                base.CustomizeAppBuilder(builder));
        }

        private void InstallLifecycleObservers()
        {
            didEnterBackgroundObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                UIApplication.DidEnterBackgroundNotification,
                _ => LoggingBootstrapper.Flush());

            willTerminateObserver ??= NSNotificationCenter.DefaultCenter.AddObserver(
                UIApplication.WillTerminateNotification,
                _ => LoggingBootstrapper.FlushAndClose());
        }
    }
}
