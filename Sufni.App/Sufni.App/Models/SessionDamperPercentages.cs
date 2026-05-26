using Sufni.Telemetry;

namespace Sufni.App.Models;

public enum DamperBand
{
    Hsc,
    Lsc,
    Hsr,
    Lsr,
}

public sealed record SessionDamperSidePercentages(
    double? HscPercentage,
    double? LscPercentage,
    double? LsrPercentage,
    double? HsrPercentage)
{
    public static SessionDamperSidePercentages Empty { get; } = new(null, null, null, null);

    public double? Get(DamperBand band)
    {
        return band switch
        {
            DamperBand.Hsc => HscPercentage,
            DamperBand.Lsc => LscPercentage,
            DamperBand.Hsr => HsrPercentage,
            DamperBand.Lsr => LsrPercentage,
            _ => null,
        };
    }
}

public sealed record SessionDamperPercentages(
    double? FrontHscPercentage,
    double? RearHscPercentage,
    double? FrontLscPercentage,
    double? RearLscPercentage,
    double? FrontLsrPercentage,
    double? RearLsrPercentage,
    double? FrontHsrPercentage,
    double? RearHsrPercentage)
{
    public static SessionDamperPercentages Empty { get; } = new(null, null, null, null, null, null, null, null);

    public static SessionDamperPercentages FromSides(
        SessionDamperSidePercentages front,
        SessionDamperSidePercentages rear)
    {
        return new SessionDamperPercentages(
            front.HscPercentage,
            rear.HscPercentage,
            front.LscPercentage,
            rear.LscPercentage,
            front.LsrPercentage,
            rear.LsrPercentage,
            front.HsrPercentage,
            rear.HsrPercentage);
    }

    public SessionDamperSidePercentages ForSide(SuspensionType side)
    {
        return side switch
        {
            SuspensionType.Front => new SessionDamperSidePercentages(
                FrontHscPercentage,
                FrontLscPercentage,
                FrontLsrPercentage,
                FrontHsrPercentage),
            SuspensionType.Rear => new SessionDamperSidePercentages(
                RearHscPercentage,
                RearLscPercentage,
                RearLsrPercentage,
                RearHsrPercentage),
            _ => SessionDamperSidePercentages.Empty,
        };
    }

    public double? Get(SuspensionType side, DamperBand band)
    {
        return ForSide(side).Get(band);
    }
}
