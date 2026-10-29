using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.TestSupport.Harness;

internal static class TestAppEnvironment
{
    public static IAppEnvironment Create(
        UiLayoutProfile layoutProfile,
        AppCapabilities? capabilities = null,
        InputCapabilities? input = null) =>
        new AppEnvironment(
            DefaultLayoutProfile: layoutProfile,
            LayoutProfile: layoutProfile,
            Capabilities: capabilities ?? new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                CanPlayVideo: true),
            Input: input ?? new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsLongPressContextMenu: false));
}
