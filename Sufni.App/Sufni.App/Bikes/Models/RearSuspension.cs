using Sufni.Kinematics;

namespace Sufni.App.Bikes.Models;

public abstract record RearSuspension;

public sealed record LinkageRearSuspension(Linkage Linkage) : RearSuspension;

public sealed record LeverageRatioRearSuspension(LeverageRatioSpec LeverageRatio) : RearSuspension;
