using Avalonia.Headless.XUnit;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.TestSupport;
using Sufni.App.Tests.TestSupport;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
namespace Sufni.App.Tests.Coordinators;

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
        var pushed = new TestViewModel();
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
        var pushed = new TestViewModel();
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
    public void Close_OnlyClosesCurrentTopView()
    {
        var root = new TestViewModel();
        var pushed = new TestViewModel();
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

    private static MobileNavigationShellHost CreateHost() => new(TestDispatcher);

    private sealed class TestViewModel : ViewModelBase
    {
        public TestViewModel()
            : base(TestDispatcher)
        {
        }
    }
}
