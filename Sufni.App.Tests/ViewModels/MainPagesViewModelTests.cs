using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.Tests.Views;
using Sufni.App.Stores;
using Sufni.App.Theming;
using Sufni.App.ViewModels.ItemLists;

namespace Sufni.App.Tests.ViewModels;

public class MainPagesViewModelTests
{
    [Fact]
    public void SelectedIndex_ActivatesLivePage_WhenSelected_AndDeactivatesIt_WhenLeft()
    {
        var liveCoordinator = TestCoordinatorSubstitutes.LiveDaq();
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), liveCoordinator);
        var viewModel = MainPagesViewModelTestFactory.Create(livePage);

        viewModel.SelectedIndex = 3;
        viewModel.SelectedIndex = 0;

        liveCoordinator.Received(1).Activate();
        liveCoordinator.Received(1).Deactivate();
    }

    [Fact]
    public void LiveDaqsPage_IsAlwaysProvided()
    {
        var livePage = new LiveDaqListViewModel(new LiveDaqStore(), TestCoordinatorSubstitutes.LiveDaq());
        var viewModel = MainPagesViewModelTestFactory.Create(livePage);

        Assert.Same(livePage, viewModel.LiveDaqsPage);
    }

    [Fact]
    public async Task OpenGpsTracksCommand_ImportsGpxThroughTrackCoordinator()
    {
        var trackCoordinator = TestCoordinatorSubstitutes.Track();
        var viewModel = MainPagesViewModelTestFactory.Create(trackCoordinator: trackCoordinator);

        await viewModel.OpenGpsTracksCommand.ExecuteAsync(null);

        await trackCoordinator.Received(1).ImportGpxAsync();
    }

    [Fact]
    public async Task OpenGpsTracksCommand_AddsNotification_WhenGpxWasAlreadyImported()
    {
        var trackCoordinator = TestCoordinatorSubstitutes.Track();
        trackCoordinator.ImportGpxAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new GpxImportResult(0, 1)));
        var viewModel = MainPagesViewModelTestFactory.Create(trackCoordinator: trackCoordinator);

        await viewModel.OpenGpsTracksCommand.ExecuteAsync(null);

        Assert.Single(viewModel.SessionsPage.Notifications);
    }

    [Fact]
    public async Task Constructor_RefreshesRecordedSourceStore_WithInitialDatabaseLoad()
    {
        var sourceStore = Substitute.For<IRecordedSessionSourceStore>();
        sourceStore.RefreshAsync().Returns(Task.CompletedTask);

        _ = MainPagesViewModelTestFactory.Create(recordedSessionSourceStore: sourceStore);

        await sourceStore.Received(1).RefreshAsync();
    }

    [Fact]
    public void ThemeState_UsesTwoModeCycle_WhenSystemThemeIsUnavailable()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.Light);
        themeService.EffectiveMode.Returns(SufniThemeMode.Light);
        themeService.IsSystemThemeAvailable.Returns(false);

        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        Assert.Equal(SufniThemeMode.Light, viewModel.CurrentThemeMode);
        Assert.Equal(SufniThemeMode.Light, viewModel.EffectiveThemeMode);
        Assert.False(viewModel.IsSystemThemeAvailable);
        Assert.Equal(SufniThemeMode.Dark, viewModel.NextThemeMode);
    }

    [Fact]
    public void ThemeState_IncludesSystemMode_WhenSystemThemeIsAvailable()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.Light);
        themeService.EffectiveMode.Returns(SufniThemeMode.Light);
        themeService.IsSystemThemeAvailable.Returns(true);

        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        Assert.True(viewModel.IsSystemThemeAvailable);
        Assert.Equal(SufniThemeMode.System, viewModel.NextThemeMode);
    }

    [Fact]
    public void ThemeState_MirrorsEffectiveMode_WhenSystemThemeIsActive()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.System);
        themeService.EffectiveMode.Returns(SufniThemeMode.Dark);
        themeService.IsSystemThemeAvailable.Returns(true);

        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        Assert.Equal(SufniThemeMode.System, viewModel.CurrentThemeMode);
        Assert.Equal(SufniThemeMode.Dark, viewModel.EffectiveThemeMode);
        Assert.Equal(SufniThemeMode.Dark, viewModel.NextThemeMode);
    }

    [Fact]
    public async Task ToggleThemeCommand_TogglesThroughThemeService()
    {
        var themeService = Substitute.For<IThemeService>();
        themeService.Mode.Returns(SufniThemeMode.Dark);
        themeService.EffectiveMode.Returns(SufniThemeMode.Dark);
        themeService.IsSystemThemeAvailable.Returns(true);
        var viewModel = MainPagesViewModelTestFactory.Create(themeService: themeService);

        await viewModel.ToggleThemeCommand.ExecuteAsync(null);

        await themeService.Received(1).ToggleAsync();
    }
}
