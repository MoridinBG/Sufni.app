using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
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
        Icon = "@drawable/icon",
        MainLauncher = true,
        ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
    public class MainActivity : AvaloniaMainActivity<App>
    {
        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            RegisteredServices.Collection.AddSingleton<ISecureStorage, SecureStorage.SecureStorage>();
            RegisteredServices.Collection.AddSingleton<IServiceDiscovery, ServiceDiscovery.ServiceDiscovery>();
            RegisteredServices.Collection.AddSingleton<IHapticFeedback, HapticFeedback.HapticFeedback>(provider => 
                new HapticFeedback.HapticFeedback(Window!));

            return base.CustomizeAppBuilder(builder)
                .UseAndroid()
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}