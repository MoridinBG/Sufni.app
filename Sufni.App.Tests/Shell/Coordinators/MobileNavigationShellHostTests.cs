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
    public void SetRoot_InitializesLogicalStack()
    {
        var root = new TestViewModel();
        var host = CreateHost();

        host.SetRoot(root);

        Assert.Same(root, host.CurrentView);
        Assert.False(host.CanGoBack);
        Assert.Equal([root], host.LogicalStack);
    }

    [AvaloniaFact]
    public void Push_UpdatesCurrentViewCanGoBackAndLogicalStack()
    {
        var root = new TestViewModel();
        var pushed = new TestTabPage();
        var host = CreateHost();
        host.SetRoot(root);

        host.Push(pushed);

        Assert.Same(pushed, host.CurrentView);
        Assert.True(host.CanGoBack);
        Assert.Equal([root, pushed], host.LogicalStack);
    }

    [AvaloniaFact]
    public void Pop_ReturnsFalseAtRoot_AndTrueAboveRoot()
    {
        var root = new TestViewModel();
        var pushed = new TestTabPage();
        var host = CreateHost();
        host.SetRoot(root);

        Assert.False(host.Pop());

        host.Push(pushed);

        Assert.True(host.Pop());
        Assert.Same(root, host.CurrentView);
        Assert.False(host.CanGoBack);
        Assert.Equal([root], host.LogicalStack);
    }

    [AvaloniaFact]
    public void Pop_ReturnsToPreviouslyFocusedWorkspaceTab()
    {
        var root = new TestViewModel();
        var first = new TestTabPage();
        var second = new TestTabPage();
        var host = CreateHost();
        host.SetRoot(root);
        host.Push(first);
        host.Push(second);

        Assert.True(host.Pop());

        Assert.Same(first, host.CurrentView);
        Assert.True(host.CanGoBack);
        Assert.Equal([root, first], host.LogicalStack);
    }

    [AvaloniaFact]
    public void Close_OnlyClosesCurrentTopView()
    {
        var root = new TestViewModel();
        var pushed = new TestTabPage();
        var other = new TestViewModel();
        var host = CreateHost();
        host.SetRoot(root);
        host.Push(pushed);

        Assert.False(host.Close(root));
        Assert.False(host.Close(other));
        Assert.True(host.Close(pushed));
        Assert.Same(root, host.CurrentView);
        Assert.Equal([root], host.LogicalStack);
    }

    [AvaloniaFact]
    public void Push_RejectsNonTabDetailSurface()
    {
        var host = CreateHost();
        host.SetRoot(new TestViewModel());

        Assert.Throws<InvalidOperationException>(() => host.Push(new TestViewModel()));
    }

    private static MobileNavigationShellHost CreateHost() => new(new ShellWorkspaceViewModel(TestDispatcher), TestDispatcher);

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
