using Sufni.Telemetry;

namespace Sufni.App.ExtensionHost.Contracts.Models;

public enum DampingBand
{
    Hsc,
    Lsc,
    Hsr,
    Lsr,
}

public sealed record SessionDampingSidePercentages(
    double? HscPercentage,
    double? LscPercentage,
    double? LsrPercentage,
    double? HsrPercentage)
{
    public static SessionDampingSidePercentages Empty { get; } = new(null, null, null, null);

    public double? Get(DampingBand band)
    {
        return band switch
        {
            DampingBand.Hsc => HscPercentage,
            DampingBand.Lsc => LscPercentage,
            DampingBand.Hsr => HsrPercentage,
            DampingBand.Lsr => LsrPercentage,
            _ => null,
        };
    }
}

public sealed record SessionDampingPercentages(
    double? FrontHscPercentage,
    double? RearHscPercentage,
    double? FrontLscPercentage,
    double? RearLscPercentage,
    double? FrontLsrPercentage,
    double? RearLsrPercentage,
    double? FrontHsrPercentage,
    double? RearHsrPercentage)
{
    public static SessionDampingPercentages Empty { get; } = new(null, null, null, null, null, null, null, null);

    public static SessionDampingPercentages FromSides(
        SessionDampingSidePercentages front,
        SessionDampingSidePercentages rear)
    {
        return new SessionDampingPercentages(
            front.HscPercentage,
            rear.HscPercentage,
            front.LscPercentage,
            rear.LscPercentage,
            front.LsrPercentage,
            rear.LsrPercentage,
            front.HsrPercentage,
            rear.HsrPercentage);
    }

    public SessionDampingSidePercentages ForSide(SuspensionType side)
    {
        return side switch
        {
            SuspensionType.Front => new SessionDampingSidePercentages(
                FrontHscPercentage,
                FrontLscPercentage,
                FrontLsrPercentage,
                FrontHsrPercentage),
            SuspensionType.Rear => new SessionDampingSidePercentages(
                RearHscPercentage,
                RearLscPercentage,
                RearLsrPercentage,
                RearHsrPercentage),
            _ => SessionDampingSidePercentages.Empty,
        };
    }

    public double? Get(SuspensionType side, DampingBand band)
    {
        return ForSide(side).Get(band);
    }
}
