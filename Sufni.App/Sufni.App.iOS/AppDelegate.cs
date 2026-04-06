using Avalonia;
using Avalonia.iOS;
using Foundation;
using FriendlyNameProvider;
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
            App.ServiceCollection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            App.ServiceCollection.AddSingleton<IFriendlyNameProvider, FriendlyNameProvider.FriendlyNameProvider>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("gosst");
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("sync");
            App.ServiceCollection.AddSingleton<IHapticFeedback, HapticFeedback.HapticFeedback>();
            App.ServiceCollection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();
            return base.CustomizeAppBuilder(builder)
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}
