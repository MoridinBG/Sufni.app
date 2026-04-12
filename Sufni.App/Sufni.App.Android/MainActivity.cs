using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Avalonia.Logging;
using Microsoft.Extensions.DependencyInjection;
using Sufni.App.Coordinators;
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
            LoggingBootstrapper.Initialize("Android");
            Logger.Sink = new AvaloniaSerilogSink(LogEventLevel.Warning);

            App.ServiceCollection.AddSingleton<ISecureStorage, AndroidSecureStorage>();
            App.ServiceCollection.AddSingleton<IFriendlyNameProvider, AndroidFriendlyNameProvider>();
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, SocketServiceDiscovery>("gosst");
            App.ServiceCollection.AddKeyedSingleton<IServiceDiscovery, SocketServiceDiscovery>("sync");
            App.ServiceCollection.AddSingleton<IHapticFeedback>(_ => new AndroidHapticFeedback(Window!));
            App.ServiceCollection.AddSingleton<ISynchronizationClientService, SynchronizationClientService>();
            App.ServiceCollection.AddSingleton<IPairingClientCoordinator, PairingClientCoordinator>();
            App.ServiceCollection.AddSingleton<PairingClientViewModel>();

            return base.CustomizeAppBuilder(builder)
                .UseAndroid()
                .WithInterFont()
                .With(new SkiaOptions { UseOpacitySaveLayer = true });
        }
    }
}