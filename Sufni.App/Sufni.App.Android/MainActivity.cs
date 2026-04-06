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
using Sufni.App.ViewModels;

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
            App.ServiceCollection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            App.ServiceCollection.AddSingleton<IFriendlyNameProvider, FriendlyNameProvider.FriendlyNameProvider>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("gosst");
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>("sync");
            App.ServiceCollection.AddSingleton<IHapticFeedback, HapticFeedback.HapticFeedback>(provider => 
                new HapticFeedback.HapticFeedback(Window!)); 
            App.ServiceCollection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();
            App.ServiceCollection.AddSingleton<PairingClientViewModel>();

            return base.CustomizeAppBuilder(builder)
                .UseAndroid()
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}