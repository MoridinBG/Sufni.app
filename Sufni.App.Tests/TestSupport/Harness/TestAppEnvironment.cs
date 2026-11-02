using Sufni.App.Infrastructure;
using NSubstitute;

namespace Sufni.App.Tests.TestSupport.Harness;

internal static class TestAppEnvironment
{
    public static PlatformRoot CreateRoot(LaunchPreferences? launchPreferences = null)
    {
        var environment = Create(UiLayoutProfile.Workspace);
        return new PlatformRoot(environment.Capabilities, environment.Input,
            launchPreferences ?? new LaunchPreferences(environment.LayoutProfile),
            _ => { }, Substitute.For<IAppPaths>());
    }

    public static IAppEnvironment Create(
        UiLayoutProfile layoutProfile,
        AppCapabilities? capabilities = null,
        InputCapabilities? input = null) =>
        new AppEnvironment(
            LayoutProfile: layoutProfile,
            Capabilities: capabilities ?? new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPickFolder: true,
                CanPlayVideo: true),
            Input: input ?? new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false,
                CommandModifier: CommandModifier.Control));
}
