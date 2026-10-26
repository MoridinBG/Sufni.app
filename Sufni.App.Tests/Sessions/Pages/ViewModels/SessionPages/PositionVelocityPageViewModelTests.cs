using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Plots;
using Sufni.App.Tests.TestSupport.Sessions;

namespace Sufni.App.Tests.Sessions.Pages.ViewModels.SessionPages;

public class PositionVelocityPageViewModelTests
{
    [Theory]
    [InlineData(true, false, PositionVelocitySideMode.Front)]
    [InlineData(false, true, PositionVelocitySideMode.Rear)]
    [InlineData(true, true, PositionVelocitySideMode.Rear)]
    public void SideMode_FollowsTheSidesWithData(bool frontReady, bool rearReady, PositionVelocitySideMode expected)
    {
        var workspace = new TestSessionAnalysisWorkspace(hasFrontAnalysis: frontReady, hasRearAnalysis: rearReady);
        var viewModel = new PositionVelocityPageViewModel(workspace)
        {
            SelectedSideMode = PositionVelocitySideMode.Rear,
        };

        Assert.Equal(expected, viewModel.SideMode);
        Assert.Equal(frontReady && rearReady, viewModel.IsSideSelectorVisible);
    }

    [Fact]
    public void SideMode_ReturnsToSelection_WhenSecondSideGainsData()
    {
        var workspace = new TestSessionAnalysisWorkspace(hasFrontAnalysis: true, hasRearAnalysis: false);
        var viewModel = new PositionVelocityPageViewModel(workspace)
        {
            SelectedSideMode = PositionVelocitySideMode.Both,
        };
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        workspace.RearAnalysisState = SurfacePresentationState.Ready;
        workspace.NotifyChanged(nameof(workspace.RearAnalysisState));

        Assert.Equal(PositionVelocitySideMode.Both, viewModel.SideMode);
        Assert.Contains(nameof(PositionVelocityPageViewModel.SideMode), changed);
    }

    [Fact]
    public void PresentationState_IsReady_WhenEitherSideHasData()
    {
        var workspace = new TestSessionAnalysisWorkspace(hasFrontAnalysis: false, hasRearAnalysis: true);

        var viewModel = new PositionVelocityPageViewModel(workspace);

        Assert.True(viewModel.PresentationState.IsReady);
    }

    [Fact]
    public void PresentationState_ShowsTheSideState_WhenNoSideHasData()
    {
        var noData = SurfacePresentationState.NoData("No analysis data for the selected range.");
        var workspace = new TestSessionAnalysisWorkspace(hasFrontAnalysis: false, hasRearAnalysis: false)
        {
            FrontAnalysisState = noData,
        };

        var viewModel = new PositionVelocityPageViewModel(workspace);

        Assert.Equal(noData, viewModel.PresentationState);
    }
}
