using Sufni.App.Tests.Views;
using Sufni.App.ViewModels;

namespace Sufni.App.Tests.ViewModels;

public class MainWindowViewModelTests
{
    [Fact]
    public void OpenView_SwitchesTabActivity()
    {
        var welcome = MainPagesViewModelTestFactory.CreateWelcomeScreen();
        var mainWindow = new MainWindowViewModel(MainPagesViewModelTestFactory.Create(), welcome);
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

    private sealed class TestTabPage : TabPageViewModelBase
    {
    }
}