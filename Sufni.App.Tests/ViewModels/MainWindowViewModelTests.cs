using Sufni.App.Tests.Views;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.ViewModels;

public class MainWindowViewModelTests
{
    private static readonly InlineUiThreadDispatcher TestDispatcher = new();

    [Fact]
    public void OpenView_SwitchesTabActivity()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var secondTab = new TestTabPage();

        Assert.True(welcome.IsTabActive);
        Assert.False(secondTab.IsTabActive);

        mainWindow.OpenView(secondTab);

        Assert.False(welcome.IsTabActive);
        Assert.True(secondTab.IsTabActive);

        mainWindow.OpenView(welcome);

        Assert.True(welcome.IsTabActive);
        Assert.False(secondTab.IsTabActive);
    }

    [Fact]
    public void CloseTabPage_RemembersClosedTabForRestore()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var tab = new TestTabPage();

        mainWindow.OpenView(tab);
        mainWindow.CloseTabPage(tab);
        mainWindow.RestoreCommand.Execute(null);

        Assert.Contains(tab, mainWindow.Tabs);
        Assert.Same(tab, mainWindow.CurrentView);
    }

    [Fact]
    public void CloseTabPage_DoesNotRememberTab_WhenRestoreHistoryIsDisabled()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var tab = new TestTabPage();

        mainWindow.OpenView(tab);
        mainWindow.CloseTabPage(tab, rememberForRestore: false);
        mainWindow.RestoreCommand.Execute(null);

        Assert.DoesNotContain(tab, mainWindow.Tabs);
        Assert.NotSame(tab, mainWindow.CurrentView);
    }

    [Fact]
    public void ForgetTabHistory_RemovesMatchingClosedTabs()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();
        var second = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.CloseTabPage(first);
        mainWindow.OpenView(second);
        mainWindow.CloseTabPage(second);

        mainWindow.ForgetTabHistory<TestTabPage>(tab => ReferenceEquals(tab, first));
        mainWindow.RestoreCommand.Execute(null);
        mainWindow.RestoreCommand.Execute(null);

        Assert.Contains(second, mainWindow.Tabs);
        Assert.DoesNotContain(first, mainWindow.Tabs);
    }

    private sealed class TestTabPage : TabPageViewModelBase
    {
        public TestTabPage()
            : base(TestDispatcher)
        {
        }
    }
}
