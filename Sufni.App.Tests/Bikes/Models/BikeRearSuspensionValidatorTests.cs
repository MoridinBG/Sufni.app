using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.Models;

public class BikeRearSuspensionValidatorTests
{
    [Fact]
    public void ValidateForSave_ReturnsValidHardtail_WhenNoRearSuspensionPayloadExists()
    {
        var snapshot = TestSnapshots.Bike();

        var result = CreateValidator().ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        Assert.IsType<RearSuspensionSpec.Hardtail>(valid.RearSuspension);
    }

    [Fact]
    public void ValidateForSave_ReturnsLinkageDraftFailure_WhenLinkageModeHasNoPayload()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.LinkageDraft()
        };

        var result = CreateValidator().ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LinkageDraft, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsLeverageRatioDraftFailure_WhenLeverageRatioModeHasNoPayload()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.LeverageRatioDraft()
        };

        var result = CreateValidator().ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LeverageRatioDraft, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsValidLinkage_WhenPayloadAndCalibrationAreComplete()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true);
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
        };

        var result = CreateValidator().ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        var validLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(valid.RearSuspension);
        Assert.Equal(linkage, validLinkage.Spec);
    }

    [Fact]
    public void ValidateForSave_ReturnsValidLinkage_WhenPayloadIsStructurallyPresentWithoutSolving()
    {
        var linkage = new LinkageSpec(
            [],
            [],
            new LinkSpec("shock-eye-a", "shock-eye-b"),
            0.5);
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage),
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
        };

        var result = CreateValidator().ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        var validLinkage = Assert.IsType<RearSuspensionSpec.Linkage>(valid.RearSuspension);
        Assert.Same(linkage, validLinkage.Spec);
    }

    [Fact]
    public void ValidateForSave_ReturnsLinkageInvalidFailure_WhenLinkagePayloadIsNull()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(null!),
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
        };

        var result = CreateValidator().ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LinkageInvalidOrUnsolvable, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsLinkageCalibrationFailure_WhenScaleInputsAreMissing()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkageSpec(includeHeadTubeJoints: true)),
        };

        var result = CreateValidator().ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LinkageMissingCalibration, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsValidLeverageRatio_WhenShockStrokeMatchesCurve()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var snapshot = TestSnapshots.LeverageRatioBike(leverageRatio, shockStroke: 10);

        var result = CreateValidator().ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        var validLeverageRatio = Assert.IsType<RearSuspensionSpec.LeverageRatio>(valid.RearSuspension);
        Assert.Same(leverageRatio, validLeverageRatio.Spec);
    }

    [Fact]
    public void ValidateForSave_ReturnsLeverageRatioShockStrokeFailure_WhenShockStrokeDiffersFromCurve()
    {
        var snapshot = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25)),
            shockStroke: 8);

        var result = CreateValidator().ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LeverageRatioShockStrokeMismatch, result);
    }

    private static void AssertFailure(
        BikeRearSuspensionValidationFailureCode expectedCode,
        BikeRearSuspensionValidationResult result)
    {
        var invalid = Assert.IsType<BikeRearSuspensionValidationResult.Invalid>(result);
        Assert.Equal(expectedCode, invalid.Failure.Code);
    }

    private static BikeRearSuspensionValidator CreateValidator() =>
        new();
}
