using System;
using Sufni.App.Bikes.Stores;
using Sufni.App.Bikes.Services;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

internal interface IBikeRearSuspensionValidator
{
    BikeRearSuspensionValidationResult ValidateForSave(BikeSnapshot snapshot);
}

internal sealed class BikeRearSuspensionValidator(IKinematicSolutionCache kinematicSolutionCache) : IBikeRearSuspensionValidator
{
    public BikeRearSuspensionValidationResult ValidateForSave(BikeSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return snapshot.RearSuspension switch
        {
            RearSuspensionSpec.Hardtail =>
                new BikeRearSuspensionValidationResult.Valid(snapshot.RearSuspension),

            RearSuspensionSpec.LinkageDraft =>
                Invalid(BikeRearSuspensionValidationFailureCode.LinkageDraft),

            RearSuspensionSpec.LeverageRatioDraft =>
                Invalid(BikeRearSuspensionValidationFailureCode.LeverageRatioDraft),

            RearSuspensionSpec.Linkage linkage =>
                ValidateLinkage(snapshot, linkage.Spec),

            RearSuspensionSpec.LeverageRatio leverageRatio =>
                ValidateLeverageRatio(snapshot, leverageRatio.Spec),

            _ => throw new ArgumentOutOfRangeException(nameof(snapshot), snapshot.RearSuspension.GetType().Name)
        };
    }

    private BikeRearSuspensionValidationResult ValidateLinkage(
        BikeSnapshot snapshot,
        LinkageSpec linkage)
    {
        if (snapshot.ShockStroke is null)
        {
            return Invalid(BikeRearSuspensionValidationFailureCode.LinkageMissingShockStroke);
        }

        if (snapshot.ImageByteCount == 0 ||
            snapshot.Chainstay is null ||
            snapshot.PixelsToMillimeters <= 0)
        {
            return Invalid(BikeRearSuspensionValidationFailureCode.LinkageMissingCalibration);
        }

        try
        {
            LinkageResolver.Resolve(linkage);
            _ = kinematicSolutionCache.GetOrSolve(linkage);
        }
        catch (Exception exception) when (exception is LinkageValidationException or InvalidOperationException or ArgumentException)
        {
            return Invalid(BikeRearSuspensionValidationFailureCode.LinkageInvalidOrUnsolvable);
        }

        return new BikeRearSuspensionValidationResult.Valid(
            new RearSuspensionSpec.Linkage(linkage));
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
                new RearSuspensionSpec.LeverageRatio(leverageRatio))
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

    public sealed record Valid(RearSuspensionSpec RearSuspension)
        : BikeRearSuspensionValidationResult;

    public sealed record Invalid(BikeRearSuspensionValidationFailure Failure)
        : BikeRearSuspensionValidationResult;
}

internal sealed record BikeRearSuspensionValidationFailure(
    BikeRearSuspensionValidationFailureCode Code,
    string? Detail = null);

internal enum BikeRearSuspensionValidationFailureCode
{
    LinkageDraft,
    LeverageRatioDraft,
    LinkageMissingShockStroke,
    LinkageMissingCalibration,
    LinkageInvalidOrUnsolvable,
    LeverageRatioShockStrokeMismatch,
}
