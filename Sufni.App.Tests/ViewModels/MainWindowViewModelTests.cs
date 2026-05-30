using System.Collections.Specialized;
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
    public void MoveTab_PlacesTabAfterTarget_AndPreservesSelectedTab()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();
        var second = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.OpenView(second);

        var moved = mainWindow.MoveTab(first, second, placeAfterTarget: true);

        Assert.True(moved);
        Assert.Collection(
            mainWindow.Tabs,
            tab => Assert.Same(welcome, tab),
            tab => Assert.Same(second, tab),
            tab => Assert.Same(first, tab));
        Assert.Same(second, mainWindow.CurrentView);
        Assert.False(first.IsTabActive);
        Assert.True(second.IsTabActive);
    }

    [Fact]
    public void MoveTab_PlacesTabBeforeTarget_AndKeepsMovedTabSelected()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();
        var second = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.OpenView(second);

        var moved = mainWindow.MoveTab(second, first, placeAfterTarget: false);

        Assert.True(moved);
        Assert.Collection(
            mainWindow.Tabs,
            tab => Assert.Same(welcome, tab),
            tab => Assert.Same(second, tab),
            tab => Assert.Same(first, tab));
        Assert.Same(second, mainWindow.CurrentView);
        Assert.True(second.IsTabActive);
    }

    [Fact]
    public void MoveTab_ReturnsFalse_WhenDropWouldKeepSameOrder()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();

        mainWindow.OpenView(first);

        var moved = mainWindow.MoveTab(first, welcome, placeAfterTarget: true);

        Assert.False(moved);
        Assert.Collection(
            mainWindow.Tabs,
            tab => Assert.Same(welcome, tab),
            tab => Assert.Same(first, tab));
    }

    [Fact]
    public void MoveTab_SuppressesTransientSelectionChangesDuringCollectionMove()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();
        var second = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.OpenView(second);
        first.ResetActivationCounts();
        second.ResetActivationCounts();

        mainWindow.Tabs.CollectionChanged += (_, args) =>
        {
            if (args.Action == NotifyCollectionChangedAction.Move)
            {
                mainWindow.CurrentView = first;
            }
        };

        var moved = mainWindow.MoveTab(second, first, placeAfterTarget: false);

        Assert.True(moved);
        Assert.Same(second, mainWindow.CurrentView);
        Assert.False(first.IsTabActive);
        Assert.True(second.IsTabActive);
        Assert.Equal(0, first.ActivatedCount);
        Assert.Equal(0, second.ActivatedCount);
        Assert.Equal(0, second.DeactivatedCount);
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
        public int ActivatedCount { get; private set; }
        public int DeactivatedCount { get; private set; }

        public TestTabPage()
            : base(TestDispatcher)
        {
        }

        public void ResetActivationCounts()
        {
            ActivatedCount = 0;
            DeactivatedCount = 0;
        }

        protected override void OnActivated()
        {
            ActivatedCount++;
        }

        protected override void OnDeactivated()
        {
            DeactivatedCount++;
        }
    }
}
