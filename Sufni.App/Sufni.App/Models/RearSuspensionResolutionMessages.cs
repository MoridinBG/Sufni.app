namespace Sufni.App.Models;

public static class RearSuspensionResolutionMessages
{
    private const string KindPayloadMismatch =
        "Rear suspension kind does not match the stored rear suspension payload.";

    public static string ForLoad(RearSuspensionResolutionError error) => error switch
    {
        RearSuspensionResolutionError.KindNoneHasPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLinkageMissingPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLinkageHasLeverageRatioPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLeverageRatioMissingPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLeverageRatioHasLinkagePayload => KindPayloadMismatch,
        RearSuspensionResolutionError.MultiplePayloadsPresent => KindPayloadMismatch,
        _ => KindPayloadMismatch,
    };

    public static string ForSave(RearSuspensionResolutionError error) => error switch
    {
        RearSuspensionResolutionError.KindNoneHasPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLinkageMissingPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLinkageHasLeverageRatioPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLeverageRatioMissingPayload => KindPayloadMismatch,
        RearSuspensionResolutionError.KindLeverageRatioHasLinkagePayload => KindPayloadMismatch,
        RearSuspensionResolutionError.MultiplePayloadsPresent => KindPayloadMismatch,
        _ => KindPayloadMismatch,
    };
}
