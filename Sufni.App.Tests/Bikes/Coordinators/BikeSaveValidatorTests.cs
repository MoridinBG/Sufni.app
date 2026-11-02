using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.Coordinators;

public class BikeSaveValidatorTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Validate_RejectsMissingGeometryIncompleteWheelsAndInvalidRearPayload(int invalidInput)
    {
        var draft = BikeDraft.From(TestSnapshots.Bike());
        draft = invalidInput switch
        {
            0 => draft with { HeadAngle = null },
            1 => draft with { ForkStroke = null },
            2 => draft with { FrontWheel = new(700, null, null), RearWheel = null },
            _ => draft with { RearSuspension = new RearSuspensionSpec.LeverageRatioDraft() },
        };
        var sut = new BikeSaveValidator(new BikeRearSuspensionValidator(), new Solver(), new InlineBackgroundTaskRunner());

        Assert.IsType<BikeSaveValidation.Invalid>(await sut.ValidateAsync(draft, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validate_LeverageRatioRequiresMatchingShockStroke(bool matches)
    {
        var draft = BikeDraft.From(TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50)), shockStroke: matches ? 20 : 15));
        var sut = new BikeSaveValidator(new BikeRearSuspensionValidator(), new Solver(), new InlineBackgroundTaskRunner());

        var result = await sut.ValidateAsync(draft, TestContext.Current.CancellationToken);

        if (matches) Assert.IsType<BikeSaveValidation.Valid>(result);
        else Assert.IsType<BikeSaveValidation.Invalid>(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Validate_PerformsSolvabilityWorkInBackground_AndDistinguishesInvalidFromUnexpectedFailure(bool unexpected)
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var draft = BikeDraft.From(TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.Linkage(linkage), ShockStroke = linkage.ShockStroke,
            PixelsToMillimeters = 1, ImageBytes = TestImages.SmallPngBytes(),
        });
        var failure = unexpected ? (Exception)new IOException("solver diagnostic")
            : new KinematicCalculationException(KinematicCalculationFailureReason.DegenerateGeometry, "geometry diagnostic");
        var runner = new Sufni.App.Tests.TestSupport.Async.DeferredBackgroundTaskRunner();
        var sut = new BikeSaveValidator(new BikeRearSuspensionValidator(), new Solver(failure), runner);

        var pending = sut.ValidateAsync(draft, TestContext.Current.CancellationToken);
        Assert.False(pending.IsCompleted);
        await runner.RunNextAsync();
        var result = await pending.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        if (unexpected) Assert.Equal("solver diagnostic", Assert.IsType<BikeSaveValidation.Failed>(result).Details);
        else Assert.IsType<BikeSaveValidation.Invalid>(result);
    }

    private sealed class Solver(Exception? failure = null) : IKinematicSolutionCache
    {
        public KinematicSolution GetOrSolve(LinkageSpec linkage, int steps = 200, int iterations = 1000) =>
            failure is null ? new KinematicSolution([]) : throw failure;
    }
}
