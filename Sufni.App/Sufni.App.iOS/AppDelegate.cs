using Avalonia;
using Avalonia.iOS;
using Foundation;
using HapticFeedback;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using ServiceDiscovery;
using Sufni.App.Services;

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
            RegisteredServices.Collection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            RegisteredServices.Collection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("gosst");
            RegisteredServices.Collection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("sync");
            RegisteredServices.Collection.AddSingleton<IHapticFeedback, HapticFeedback.HapticFeedback>();
            RegisteredServices.Collection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();
            return base.CustomizeAppBuilder(builder)
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}