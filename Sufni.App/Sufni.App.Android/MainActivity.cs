using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using FriendlyNameProvider;
using HapticFeedback;
using Microsoft.Extensions.DependencyInjection;
using SecureStorage;
using ServiceDiscovery;
using Sufni.App.Services;

namespace Sufni.App.Android
{
    [Activity(
        Label = "Sufni Telemetry",
        Theme = "@style/MyTheme.NoActionBar",
        Icon = "@mipmap/ic_launcher",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            RegisteredServices.Collection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            RegisteredServices.Collection.AddSingleton<IFriendlyNameProvider, FriendlyNameProvider.FriendlyNameProvider>();
            RegisteredServices.Collection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("gosst");
            RegisteredServices.Collection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("sync");
            RegisteredServices.Collection.AddSingleton<IHapticFeedback, HapticFeedback.HapticFeedback>(provider => 
                new HapticFeedback.HapticFeedback(Window!)); 
            RegisteredServices.Collection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();

            return base.CustomizeAppBuilder(builder)
                .UseAndroid()
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}