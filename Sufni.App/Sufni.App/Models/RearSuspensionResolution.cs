namespace Sufni.App.Models;

public abstract record RearSuspensionResolution
{
    public sealed record Hardtail : RearSuspensionResolution;

    public sealed record Linkage(LinkageRearSuspension Value) : RearSuspensionResolution;

    public sealed record LeverageRatio(LeverageRatioRearSuspension Value) : RearSuspensionResolution;

    public sealed record Invalid(RearSuspensionResolutionError Error) : RearSuspensionResolution;
}

public enum RearSuspensionResolutionError
{
    KindNoneHasPayload,
    KindLinkageMissingPayload,
    KindLinkageHasLeverageRatioPayload,
    KindLeverageRatioMissingPayload,
    KindLeverageRatioHasLinkagePayload,
    MultiplePayloadsPresent,
}
