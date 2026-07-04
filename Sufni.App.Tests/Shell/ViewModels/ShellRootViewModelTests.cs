using Sufni.App.Infrastructure;
using Sufni.App.Shell.ViewModels;

namespace Sufni.App.Tests.Shell.ViewModels;

public class ShellRootViewModelTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [Fact]
    public void Constructor_ExposesSharedShellStateAndEnvironment()
    {
        var pages = MainPagesViewModelTestFactory.Create();
        var workspace = new ShellWorkspaceViewModel(TestDispatcher);
        var environment = new AppEnvironment(
            DefaultLayoutProfile: UiLayoutProfile.Compact,
            LayoutProfile: UiLayoutProfile.Workspace,
            Capabilities: new AppCapabilities(
                CanHostSyncServer: true,
                CanPairAsClient: false,
                HasHaptics: false,
                SupportsMassStorageImport: true,
                SupportsStorageProviderImport: true,
                SupportsNativeWindowing: true),
            Input: new InputCapabilities(
                HasPointer: true,
                HasTouch: false,
                HasKeyboard: true,
                SupportsPinch: false,
                SupportsLongPressContextMenu: false));

        var root = new ShellRootViewModel(pages, workspace, environment, TestDispatcher);

        Assert.Same(pages, root.Pages);
        Assert.Same(workspace, root.Workspace);
        Assert.Equal(UiLayoutProfile.Workspace, root.LayoutProfile);
        Assert.Same(environment.Capabilities, root.Capabilities);
    }
}
