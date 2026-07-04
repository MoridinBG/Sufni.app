using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using Sufni.App.Infrastructure;

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
            App.ServiceCollection.AddAppEnvironment(
                UiLayoutProfile.Compact,
                new AppCapabilities(
                    CanHostSyncServer: false,
                    CanPairAsClient: true,
                    SupportsMassStorageImport: false,
                    SupportsStorageProviderImport: true),
                new InputCapabilities(
                    HasPointer: false,
                    HasTouch: true,
                    HasKeyboard: false,
                    SupportsLongPressContextMenu: true));
            MobileAppBootstrapper.RegisterMobileSync(
                App.ServiceCollection,
                static () => new AndroidSecureStorage(),
                static () => new AndroidFriendlyNameProvider(),
                static _ => new SocketServiceDiscovery(),
                () => new AndroidHapticFeedback(Window!));

            return MobileAppBootstrapper.ConfigureMobileAvalonia(
                base.CustomizeAppBuilder(builder).UseAndroid());
        }
    }
}
