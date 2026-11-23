using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Queries;
using Sufni.App.Bikes.Services;
using Sufni.App.Shared.Models;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.Queries;

public class RearSuspensionAnalysisQueryTests
{
    private static IRearSuspensionAnalysisQuery CreateQuery(IKinematicSolutionCache? cache = null) =>
        new RearSuspensionAnalysisQuery(new InlineBackgroundTaskRunner(), cache ?? new KinematicSolutionCache());

    public static TheoryData<RearSuspensionSpec, Type> UnavailableRearSuspensions => new()
    {
        { new RearSuspensionSpec.Hardtail(), typeof(BikeEditorAnalysisResult.NotApplicable) },
        { new RearSuspensionSpec.LinkageDraft(), typeof(BikeEditorAnalysisResult.Unsolvable) },
        { new RearSuspensionSpec.LeverageRatioDraft(), typeof(BikeEditorAnalysisResult.Unsolvable) },
    };

    [Theory]
    [MemberData(nameof(UnavailableRearSuspensions))]
    public async Task LoadAnalysisAsync_ClassifiesUnavailableInputs_WithoutSolving(RearSuspensionSpec rearSuspension, Type expected)
    {
        var result = await CreateQuery().LoadAnalysisAsync(rearSuspension, TestContext.Current.CancellationToken);

        Assert.IsType(expected, result);
    }

    [Fact]
    public async Task LoadAnalysisAsync_UnexpectedSolverFailure_IsNotReportedAsUnsolvable()
    {
        var cache = new FaultingKinematicSolutionCache();

        var result = await CreateQuery(cache).LoadAnalysisAsync(new RearSuspensionSpec.Linkage(TestSnapshots.FullSuspensionLinkageSpec()), TestContext.Current.CancellationToken);

        Assert.Equal(BikeAnalysisFailureReason.Analyze, Assert.IsType<BikeEditorAnalysisResult.Failed>(result).Reason);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsUnsolvable_WhenLinkageCannotBeSolved()
    {
        var linkage = new LinkageSpec(
            [],
            [],
            new LinkSpec("shock-eye-a", "shock-eye-b"),
            0.5);

        var result = await CreateQuery().LoadAnalysisAsync(new RearSuspensionSpec.Linkage(linkage), cancellationToken: TestContext.Current.CancellationToken);

        Assert.IsType<BikeEditorAnalysisResult.Unsolvable>(result);
    }

    [Fact]
    public async Task LoadAnalysisAsync_ReturnsComputed_WhenLeverageRatioIsValid()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve(
            (0, 0),
            (10, 25),
            (20, 45));

        var result = await CreateQuery().LoadAnalysisAsync(new RearSuspensionSpec.LeverageRatio(leverageRatio), cancellationToken: TestContext.Current.CancellationToken);

        var computed = Assert.IsType<BikeEditorAnalysisResult.Computed>(result);
        Assert.Collection(
            computed.Data.LeverageRatioData.X,
            wheelTravel => Assert.Equal(12.5, wheelTravel),
            wheelTravel => Assert.Equal(35, wheelTravel));
        Assert.Collection(
            computed.Data.LeverageRatioData.Y,
            ratio => Assert.Equal(2.5, ratio),
            ratio => Assert.Equal(2, ratio));
        Assert.Null(computed.Data.RearAxlePathData);
    }

    private sealed class FaultingKinematicSolutionCache : IKinematicSolutionCache
    {
        public KinematicSolution GetOrSolve(LinkageSpec linkage, int steps = 200, int iterations = 1000) =>
            throw new InvalidOperationException("unexpected failure");
    }

}
