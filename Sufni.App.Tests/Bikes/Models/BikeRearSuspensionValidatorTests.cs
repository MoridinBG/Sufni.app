using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Bikes.Models;

public class BikeRearSuspensionValidatorTests
{
    [Fact]
    public void ValidateForSave_ReturnsValidHardtail_WhenNoRearSuspensionPayloadExists()
    {
        var snapshot = TestSnapshots.Bike();

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        Assert.IsType<RearSuspensionSpec.Hardtail>(valid.RearSuspension);
        Assert.Null(valid.AnalysisInput);
    }

    [Fact]
    public void ValidateForSave_ReturnsLinkageDraftFailure_WhenLinkageModeHasNoPayload()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.LinkageDraft()
        };

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LinkageDraft, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsLeverageRatioDraftFailure_WhenLeverageRatioModeHasNoPayload()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            RearSuspension = new RearSuspensionSpec.LeverageRatioDraft()
        };

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LeverageRatioDraft, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsValidLinkage_WhenPayloadAndCalibrationAreComplete()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true);
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage.ToSpec()),
            Chainstay = 440,
            PixelsToMillimeters = 1,
            ImageBytes = TestImages.SmallPngBytes(),
        };

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        Assert.IsType<RearSuspensionSpec.Linkage>(valid.RearSuspension);
        var analysisInput = Assert.IsType<LinkageRearSuspension>(valid.AnalysisInput);
        Assert.Equal(linkage.ToSpec(), analysisInput.Linkage.ToSpec());
    }

    [Fact]
    public void ValidateForSave_ReturnsLinkageCalibrationFailure_WhenScaleInputsAreMissing()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = 0.5,
            RearSuspension = new RearSuspensionSpec.Linkage(
                TestSnapshots.FullSuspensionLinkage(includeHeadTubeJoints: true).ToSpec()),
        };

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LinkageMissingCalibration, result);
    }

    [Fact]
    public void ValidateForSave_ReturnsValidLeverageRatio_WhenShockStrokeMatchesCurve()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var snapshot = TestSnapshots.LeverageRatioBike(leverageRatio, shockStroke: 10);

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        var valid = Assert.IsType<BikeRearSuspensionValidationResult.Valid>(result);
        Assert.IsType<RearSuspensionSpec.LeverageRatio>(valid.RearSuspension);
        var analysisInput = Assert.IsType<LeverageRatioRearSuspension>(valid.AnalysisInput);
        Assert.Same(leverageRatio, analysisInput.LeverageRatio);
    }

    [Fact]
    public void ValidateForSave_ReturnsLeverageRatioShockStrokeFailure_WhenShockStrokeDiffersFromCurve()
    {
        var snapshot = TestSnapshots.LeverageRatioBike(
            TestSnapshots.LeverageRatioCurve((0, 0), (10, 25)),
            shockStroke: 8);

        var result = BikeRearSuspensionValidator.ValidateForSave(snapshot);

        AssertFailure(BikeRearSuspensionValidationFailureCode.LeverageRatioShockStrokeMismatch, result);
    }

    private static void AssertFailure(
        BikeRearSuspensionValidationFailureCode expectedCode,
        BikeRearSuspensionValidationResult result)
    {
        var invalid = Assert.IsType<BikeRearSuspensionValidationResult.Invalid>(result);
        Assert.Equal(expectedCode, invalid.Failure.Code);
    }
}
