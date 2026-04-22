using System;
using Sufni.Kinematics;

namespace Sufni.App.Models;

public static class RearSuspensionResolver
{
    public static RearSuspensionResolution Resolve(
        RearSuspensionKind kind,
        Linkage? linkage,
        LeverageRatio? leverageRatio)
    {
        var hasLinkage = linkage is not null;
        var hasLeverageRatio = leverageRatio is not null;

        if (hasLinkage && hasLeverageRatio)
        {
            return new RearSuspensionResolution.Invalid(RearSuspensionResolutionError.MultiplePayloadsPresent);
        }

        switch (kind)
        {
            case RearSuspensionKind.None:
                if (hasLinkage || hasLeverageRatio)
                {
                    return new RearSuspensionResolution.Invalid(RearSuspensionResolutionError.KindNoneHasPayload);
                }
                return new RearSuspensionResolution.Hardtail();

            case RearSuspensionKind.Linkage:
                if (hasLeverageRatio)
                {
                    return new RearSuspensionResolution.Invalid(RearSuspensionResolutionError.KindLinkageHasLeverageRatioPayload);
                }
                if (!hasLinkage)
                {
                    return new RearSuspensionResolution.Invalid(RearSuspensionResolutionError.KindLinkageMissingPayload);
                }
                return new RearSuspensionResolution.Linkage(new LinkageRearSuspension(linkage!));

            case RearSuspensionKind.LeverageRatio:
                if (hasLinkage)
                {
                    return new RearSuspensionResolution.Invalid(RearSuspensionResolutionError.KindLeverageRatioHasLinkagePayload);
                }
                if (!hasLeverageRatio)
                {
                    return new RearSuspensionResolution.Invalid(RearSuspensionResolutionError.KindLeverageRatioMissingPayload);
                }
                return new RearSuspensionResolution.LeverageRatio(new LeverageRatioRearSuspension(leverageRatio!));

            default:
                throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
    }
}
