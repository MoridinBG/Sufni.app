using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Sufni.App.Infrastructure;

namespace Sufni.App.Android;

[Application]
public sealed class MainApplication(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<App>(javaReference, transfer)
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
            static () => new AndroidHapticFeedback(() => MainActivity.CurrentWindow));

        return MobileAppBootstrapper.ConfigureMobileAvalonia(
            base.CustomizeAppBuilder(builder));
    }
}
