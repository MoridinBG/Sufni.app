using Avalonia;
using Avalonia.iOS;
using Foundation;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using ServiceDiscovery;
using Sufni.App.Coordinators;
using Sufni.App.Services;
using Sufni.App.ViewModels;

namespace Sufni.App.iOS
{
    // The UIApplicationDelegate for the application. This class is responsible for launching the 
    // User Interface of the application, as well as listening (and optionally responding) to 
    // application events from iOS.
    [Register("AppDelegate")]
    public partial class AppDelegate : AvaloniaAppDelegate<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            App.ServiceCollection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            App.ServiceCollection.AddSingleton<IFriendlyNameProvider, IosFriendlyNameProvider>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("gosst");
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("sync");
            App.ServiceCollection.AddSingleton<IHapticFeedback, IosHapticFeedback>();
            App.ServiceCollection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();
            App.ServiceCollection.AddSingleton<IPairingClientCoordinator, PairingClientCoordinator>();
            App.ServiceCollection.AddSingleton<PairingClientViewModel>();
            return base.CustomizeAppBuilder(builder)
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}
