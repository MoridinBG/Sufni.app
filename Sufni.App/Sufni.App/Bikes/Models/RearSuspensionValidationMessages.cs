using System;

namespace Sufni.App.Bikes.Models;

internal static class RearSuspensionValidationMessages
{
    public static string ForSave(BikeRearSuspensionValidationFailure failure) => failure.Code switch
    {
        BikeRearSuspensionValidationFailureCode.LinkageDraft => "Linkage data is required for linkage bikes.",
        BikeRearSuspensionValidationFailureCode.LeverageRatioDraft => "Leverage ratio data is required for leverage ratio bikes.",
        BikeRearSuspensionValidationFailureCode.LinkageMissingShockStroke => "Shock stroke is required for linkage bikes.",
        BikeRearSuspensionValidationFailureCode.LinkageMissingCalibration => "Linkage bikes require an image, chainstay, and calibrated linkage scale.",
        BikeRearSuspensionValidationFailureCode.LinkageInvalidOrUnsolvable => "Linkage movement could not be calculated. Please check the joints and links.",
        BikeRearSuspensionValidationFailureCode.LeverageRatioShockStrokeMismatch => failure.Detail ?? "Shock stroke must match leverage ratio max shock stroke.",
        _ => throw new ArgumentOutOfRangeException(nameof(failure), failure.Code, null),
    };
}
