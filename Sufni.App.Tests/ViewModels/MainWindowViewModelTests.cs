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
    public void CloseTabPage_RemembersSameTabReferenceOnlyOnce()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var tab = new TestTabPage();

        mainWindow.OpenView(tab);
        mainWindow.CloseTabPage(tab);
        mainWindow.CloseTabPage(tab);
        mainWindow.RestoreCommand.Execute(null);
        mainWindow.CloseTabPage(tab, rememberForRestore: false);
        mainWindow.RestoreCommand.Execute(null);

        Assert.DoesNotContain(tab, mainWindow.Tabs);
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
    public void SelectNextTabCommand_SelectsNextTabAndWraps()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();
        var second = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.OpenView(second);
        mainWindow.OpenView(welcome);

        mainWindow.SelectNextTabCommand.Execute(null);

        Assert.Same(first, mainWindow.CurrentView);
        Assert.True(first.IsTabActive);
        Assert.False(welcome.IsTabActive);

        mainWindow.SelectNextTabCommand.Execute(null);
        mainWindow.SelectNextTabCommand.Execute(null);

        Assert.Same(welcome, mainWindow.CurrentView);
        Assert.True(welcome.IsTabActive);
        Assert.False(second.IsTabActive);
    }

    [Fact]
    public void SelectPreviousTabCommand_SelectsPreviousTabAndWraps()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();
        var second = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.OpenView(second);
        mainWindow.OpenView(welcome);

        mainWindow.SelectPreviousTabCommand.Execute(null);

        Assert.Same(second, mainWindow.CurrentView);
        Assert.True(second.IsTabActive);
        Assert.False(welcome.IsTabActive);

        mainWindow.SelectPreviousTabCommand.Execute(null);
        mainWindow.SelectPreviousTabCommand.Execute(null);

        Assert.Same(welcome, mainWindow.CurrentView);
        Assert.True(welcome.IsTabActive);
        Assert.False(first.IsTabActive);
    }

    [Fact]
    public void SelectTabCommands_DoNothing_WhenOnlyOneTabIsOpen()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);

        mainWindow.SelectNextTabCommand.Execute(null);
        mainWindow.SelectPreviousTabCommand.Execute(null);

        Assert.Same(welcome, mainWindow.CurrentView);
        Assert.True(welcome.IsTabActive);
    }

    [Fact]
    public void SelectTabCommands_DoNothing_WhenNoTabIsSelected()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var first = new TestTabPage();

        mainWindow.OpenView(first);
        mainWindow.CurrentView = null;

        mainWindow.SelectNextTabCommand.Execute(null);
        mainWindow.SelectPreviousTabCommand.Execute(null);

        Assert.Null(mainWindow.CurrentView);
        Assert.False(welcome.IsTabActive);
        Assert.False(first.IsTabActive);
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

    [Fact]
    public void TakeTabHistory_ReturnsMostRecentMatch_AndRemovesAllMatchingClosedTabs()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome, TestDispatcher);
        var olderMatch = new TestTabPage("session");
        var unrelated = new TestTabPage("other");
        var newerMatch = new TestTabPage("session");

        mainWindow.OpenView(olderMatch);
        mainWindow.CloseTabPage(olderMatch);
        mainWindow.OpenView(unrelated);
        mainWindow.CloseTabPage(unrelated);
        mainWindow.OpenView(newerMatch);
        mainWindow.CloseTabPage(newerMatch);

        var taken = mainWindow.TakeTabHistory<TestTabPage>(tab => tab.HistoryKey == "session");
        mainWindow.RestoreCommand.Execute(null);
        mainWindow.RestoreCommand.Execute(null);

        Assert.Same(newerMatch, taken);
        Assert.Contains(unrelated, mainWindow.Tabs);
        Assert.DoesNotContain(olderMatch, mainWindow.Tabs);
        Assert.DoesNotContain(newerMatch, mainWindow.Tabs);
    }

    private sealed class TestTabPage : TabPageViewModelBase
    {
        public int ActivatedCount { get; private set; }
        public int DeactivatedCount { get; private set; }
        public string? HistoryKey { get; }

        public TestTabPage(string? historyKey = null)
            : base(TestDispatcher)
        {
            HistoryKey = historyKey;
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
