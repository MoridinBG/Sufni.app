using Sufni.App.Shell.Services.Preferences;
using System;
using System.IO;
using Android.App;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using Sufni.App.Infrastructure;

namespace Sufni.App.Android;

[Application]
public sealed class MainApplication(nint javaReference, JniHandleOwnership transfer)
    : AvaloniaAndroidApplication<Avalonia.Application>(javaReference, transfer)
{
    protected override AppBuilder CreateAppBuilder()
    {
        var paths = new AppPaths(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sufni.App"));
        LoggingBootstrapper.Initialize("Android", paths, TimeProvider.System);
        var root = new PlatformRoot(
            new AppCapabilities(
                CanHostSyncServer: false,
                CanPairAsClient: true,
                SupportsMassStorageImport: false,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: false),
            new InputCapabilities(
                HasPointer: false,
                HasTouch: true,
                HasKeyboard: false,
                SupportsLongPressContextMenu: true,
                CommandModifier: CommandModifier.Control),
            AppPreferences.ReadLaunchValues(paths.PreferencesPath, UiLayoutProfile.Compact),
            services =>
            {
                MobileAppBootstrapper.RegisterMobileSync(
                    services,
                    static () => new AndroidSecureStorage(),
                    static () => new AndroidFriendlyNameProvider(),
                    static role => new AndroidServiceDiscovery(
                        new SocketServiceDiscovery(role == "daq"
                            ? ServiceDiscoveryResolution.AnnouncedAddresses
                            : ServiceDiscoveryResolution.ReachableTcpEndpoint),
                        new AndroidMulticastLockFactory()),
                    static () => new AndroidHapticFeedback(() => MainActivity.CurrentWindow));
            },
            paths);

        return MobileAppBootstrapper.ConfigureMobileAvalonia(
            AppBuilder.Configure(() => new App(root)).UseAndroid());
    }
}
