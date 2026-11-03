using Microsoft.Extensions.Time.Testing;
using NSubstitute;

using Sufni.App.SyncAndPairing.Coordinators;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Bikes.Stores;
using Sufni.App.Infrastructure;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.LiveDaq.Stores;
using Sufni.App.LiveDaq.ViewModels.ItemLists;
using Sufni.App.MapsAndTracks.Coordinators;
using Sufni.App.Sessions.Store;
using Sufni.App.Setups.Stores;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shell.ViewModels;
using Sufni.App.SyncAndPairing.Stores;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Video.Coordinators;
namespace Sufni.App.Tests.Shell.ViewModels;

public class MainPagesViewModelTests
{
    private readonly IShellCoordinator shell = Substitute.For<IShellCoordinator>();
    private static readonly InlineUiThreadDispatcher UiThreadDispatcher = new();

    [Fact]
    public async Task OpenGpsTracksCommand_AddsNotification_WhenGpxWasAlreadyImported()
    {
        var trackCoordinator = TestCoordinatorSubstitutes.Track();
        trackCoordinator.ImportGpxAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<GpxImportResult>(new GpxImportResult.Completed(0, 1)));
        var viewModel = MainPagesViewModelTestFactory.Create(trackCoordinator: trackCoordinator);
        viewModel.SelectedPrimaryIndex = 2;

        await viewModel.OpenGpsTracksCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SessionsPage.Notifications);
        Assert.Single(viewModel.Feedback.Notifications);
        Assert.Empty(viewModel.BikesPage.Notifications);
    }

    [Fact]
    public async Task OpenVideoCommand_AddsShellNotification_WhenVideoIsLinked()
    {
        var videoCoordinator = Substitute.For<IVideoCoordinator>();
        videoCoordinator.ImportAsync(Arg.Any<CancellationToken>()).Returns(new VideoImportResult.Linked(2));
        var viewModel = MainPagesViewModelTestFactory.Create(videoCoordinator: videoCoordinator);
        viewModel.SelectedPrimaryIndex = 2;

        await viewModel.OpenVideoCommand.ExecuteAsync(null);

        Assert.Empty(viewModel.SessionsPage.Notifications);
        Assert.Single(viewModel.Feedback.Notifications);
        Assert.Empty(viewModel.BikesPage.Notifications);
    }

    [Fact]
    public async Task OpenVideoCommand_AddsErrorMessage_WhenImportFails()
    {
        var videoCoordinator = Substitute.For<IVideoCoordinator>();
        videoCoordinator.ImportAsync(Arg.Any<CancellationToken>()).Returns(new VideoImportResult.Failed(Sufni.App.Shared.Models.VideoImportFailureReason.Import));
        var viewModel = MainPagesViewModelTestFactory.Create(videoCoordinator: videoCoordinator);

        await viewModel.OpenVideoCommand.ExecuteAsync(null);

        Assert.Single(viewModel.Feedback.ErrorMessages);
        Assert.Empty(viewModel.SessionsPage.ErrorMessages);
    }

}
