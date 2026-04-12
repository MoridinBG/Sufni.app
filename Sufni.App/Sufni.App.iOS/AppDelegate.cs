using Avalonia;
using Avalonia.iOS;
using Avalonia.Logging;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.ViewModels;
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
            LoggingBootstrapper.PlatformSink = new OsLogSink(LoggingBootstrapper.OutputTemplate);
            LoggingBootstrapper.Initialize("iOS");
            InstallLifecycleObservers();
            Logger.Sink = new AvaloniaSerilogSink(LogEventLevel.Warning);

            App.ServiceCollection.AddSingleton<ISecureStorage, IosSecureStorage>();
            App.ServiceCollection.AddSingleton<IFriendlyNameProvider, IosFriendlyNameProvider>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, BonjourServiceDiscovery>("gosst");
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, BonjourServiceDiscovery>("sync");
            App.ServiceCollection.AddSingleton<IHapticFeedback, IosHapticFeedback>();
            App.ServiceCollection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();
            App.ServiceCollection.AddSingleton<IPairingClientCoordinator, PairingClientCoordinator>();
            App.ServiceCollection.AddSingleton<PairingClientViewModel>();
            return base.CustomizeAppBuilder(builder)
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
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
