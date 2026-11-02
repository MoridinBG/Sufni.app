using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Services;
using Sufni.App.Infrastructure;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.Coordinators;

internal interface IBikeSaveValidator
{
    Task<BikeSaveValidation> ValidateAsync(BikeDraft draft, CancellationToken cancellationToken = default);
}

internal sealed class BikeSaveValidator(
    IBikeRearSuspensionValidator rearValidator,
    IKinematicSolutionCache solutions,
    IBackgroundTaskRunner background) : IBikeSaveValidator
{
    private static readonly ILogger logger = Log.ForContext<BikeSaveValidator>();

    public static bool HasRequiredInputs(double? headAngle, double? forkStroke, RearSuspensionKind rearKind,
        double? frontDiameter, double? rearDiameter) => headAngle is not null && forkStroke is not null &&
        (rearKind == RearSuspensionKind.LeverageRatio || frontDiameter.HasValue == rearDiameter.HasValue);

    public async Task<BikeSaveValidation> ValidateAsync(BikeDraft draft, CancellationToken cancellationToken = default)
    {
        try
        {
            return await background.RunAsync<BikeSaveValidation>(() =>
            {
                if (draft.HeadAngle is null || draft.ForkStroke is null)
                    return Invalid("Head angle and fork stroke are required.");
                if (!HasRequiredInputs(draft.HeadAngle, draft.ForkStroke, draft.RearSuspension.Kind, draft.FrontWheel?.DiameterMm, draft.RearWheel?.DiameterMm))
                    return Invalid("Specify both wheel diameters or leave both empty.");
                if (rearValidator.ValidateForSave(draft.ToSnapshot()) is BikeRearSuspensionValidationResult.Invalid invalid)
                    return Invalid(RearSuspensionValidationMessages.ForSave(invalid.Failure));
                if (draft.RearSuspension is RearSuspensionSpec.Linkage linkage)
                    _ = new BikeCharacteristics(solutions.GetOrSolve(linkage.Spec)).LeverageRatioData;
                else if (draft.RearSuspension is RearSuspensionSpec.LeverageRatio curve)
                    _ = curve.Spec.DeriveLeverageRatioData();
                return new BikeSaveValidation.Valid();
            }, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is LinkageValidationException or LeverageRatioValidationException or KinematicCalculationException)
        {
            logger.Warning(exception, "Bike validation rejected unsolvable suspension for {BikeId}", draft.Id);
            return new BikeSaveValidation.Invalid(RearSuspensionValidationMessages.ForSave(
                new BikeRearSuspensionValidationFailure(BikeRearSuspensionValidationFailureCode.LinkageInvalidOrUnsolvable)));
        }
        catch (Exception exception)
        {
            logger.Error(exception, "Bike validation failed for {BikeId}", draft.Id);
            return new BikeSaveValidation.Failed(exception.Message);
        }

        BikeSaveValidation Invalid(string message)
        {
            logger.Warning("Bike validation rejected {BikeId}: {Reason}", draft.Id, message);
            return new BikeSaveValidation.Invalid(message);
        }
    }
}

internal abstract record BikeSaveValidation
{
    private BikeSaveValidation() { }
    public sealed record Valid : BikeSaveValidation;
    public sealed record Invalid(string Message) : BikeSaveValidation;
    public sealed record Failed(string Details) : BikeSaveValidation;
}
