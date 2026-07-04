using Avalonia.Headless.XUnit;
using Sufni.App.ExtensionHost.TestSupport;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
namespace Sufni.App.Tests.Shell.Coordinators;

[Collection("Ui")]
public class MobileNavigationShellHostTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [AvaloniaFact]
    public void SetRoot_ClearsCurrentWorkspaceTab()
    {
        var root = new TestViewModel();
        var workspace = CreateWorkspace();
        var tab = new TestTabPage();
        workspace.OpenOrFocus(tab);
        var host = CreateHost(workspace);

        host.SetRoot(root);

        Assert.Null(workspace.CurrentTab);
        Assert.Contains(tab, workspace.Tabs);
    }

    [AvaloniaFact]
    public void WorkspaceCurrentTab_DrivesHostState()
    {
        var root = new TestViewModel();
        var first = new TestTabPage();
        var second = new TestTabPage();
        var workspace = CreateWorkspace();
        var host = CreateHost(workspace);
        host.SetRoot(root);

        workspace.OpenOrFocus(first);
        workspace.OpenOrFocus(second);

        Assert.Same(second, workspace.CurrentTab);

        Assert.True(workspace.GoBack());
        Assert.Same(first, workspace.CurrentTab);
    }

    [AvaloniaFact]
    public void HostInterface_ExposesOnlyRootSetup()
    {
        var methods = typeof(IMobileNavigationShellHost)
            .GetMethods()
            .Select(method => method.Name)
            .Order()
            .ToArray();

        Assert.Equal(["SetRoot"], methods);
    }

    private static ShellWorkspaceViewModel CreateWorkspace() => new(TestDispatcher);

    private static MobileNavigationShellHost CreateHost(ShellWorkspaceViewModel workspace) => new(workspace, TestDispatcher);

    private sealed class TestViewModel : ViewModelBase
    {
        public TestViewModel()
            : base(TestDispatcher)
        {
        }
    }

    private sealed class TestTabPage : TabPageViewModelBase
    {
        public TestTabPage()
            : base(TestDispatcher)
        {
        }
    }
}
