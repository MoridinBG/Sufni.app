using System;
using Sufni.App.Bikes.Stores;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

internal static class BikeRearSuspensionValidator
{
    public static BikeRearSuspensionValidationResult ValidateForSave(BikeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.RearSuspension switch
        {
            RearSuspensionSpec.Hardtail =>
                new BikeRearSuspensionValidationResult.Valid(snapshot.RearSuspension, AnalysisInput: null),

            RearSuspensionSpec.LinkageDraft =>
                Invalid(BikeRearSuspensionValidationFailureCode.LinkageDraft),

            RearSuspensionSpec.LeverageRatioDraft =>
                Invalid(BikeRearSuspensionValidationFailureCode.LeverageRatioDraft),

            RearSuspensionSpec.Linkage linkage =>
                ValidateLinkage(snapshot, linkage.Spec),

            RearSuspensionSpec.LeverageRatio leverageRatio =>
                ValidateLeverageRatio(snapshot, leverageRatio.Spec),

            _ => Invalid(BikeRearSuspensionValidationFailureCode.InvalidLegacyShape),
        };
    }

    private static BikeRearSuspensionValidationResult ValidateLinkage(
        BikeSnapshot snapshot,
        LinkageSpec linkage)
    {
        if (snapshot.ShockStroke is null)
        {
            return Invalid(BikeRearSuspensionValidationFailureCode.LinkageMissingShockStroke);
        }

        if (snapshot.ImageBytes.Length == 0 ||
            snapshot.Chainstay is null ||
            snapshot.PixelsToMillimeters <= 0)
        {
            return Invalid(BikeRearSuspensionValidationFailureCode.LinkageMissingCalibration);
        }

        try
        {
            LinkageResolver.Resolve(linkage);
            _ = new KinematicSolver(linkage).SolveSuspensionMotion();
        }
        catch (Exception exception) when (exception is LinkageValidationException or InvalidOperationException or ArgumentException)
        {
            return Invalid(BikeRearSuspensionValidationFailureCode.LinkageInvalidOrUnsolvable);
        }

        return new BikeRearSuspensionValidationResult.Valid(
            new RearSuspensionSpec.Linkage(linkage),
            new LinkageRearSuspension(Linkage.FromSpec(linkage)));
    }

    private static BikeRearSuspensionValidationResult ValidateLeverageRatio(
        BikeSnapshot snapshot,
        LeverageRatioSpec leverageRatio)
    {
        return LeverageRatioShockStrokeRules.TryValidate(
            snapshot.ShockStroke,
            leverageRatio,
            out _,
            out var errorMessage)
            ? new BikeRearSuspensionValidationResult.Valid(
                new RearSuspensionSpec.LeverageRatio(leverageRatio),
                new LeverageRatioRearSuspension(leverageRatio))
            : Invalid(BikeRearSuspensionValidationFailureCode.LeverageRatioShockStrokeMismatch, errorMessage);
    }

    private static BikeRearSuspensionValidationResult Invalid(
        BikeRearSuspensionValidationFailureCode code,
        string? detail = null) =>
        new BikeRearSuspensionValidationResult.Invalid(new BikeRearSuspensionValidationFailure(code, detail));

}

internal abstract record BikeRearSuspensionValidationResult
{
    private BikeRearSuspensionValidationResult() { }

    public sealed record Valid(RearSuspensionSpec RearSuspension, RearSuspension? AnalysisInput)
        : BikeRearSuspensionValidationResult;

    public sealed record Invalid(BikeRearSuspensionValidationFailure Failure)
        : BikeRearSuspensionValidationResult;
}

internal sealed record BikeRearSuspensionValidationFailure(
    BikeRearSuspensionValidationFailureCode Code,
    string? Detail = null);

internal enum BikeRearSuspensionValidationFailureCode
{
    InvalidLegacyShape,
    LinkageDraft,
    LeverageRatioDraft,
    LinkageMissingShockStroke,
    LinkageMissingCalibration,
    LinkageInvalidOrUnsolvable,
    LeverageRatioShockStrokeMismatch,
}
