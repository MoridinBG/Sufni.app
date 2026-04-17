using Sufni.Kinematics;

namespace Sufni.App.Models;

public abstract record RearSuspension;

public sealed record LinkageRearSuspension(Linkage Linkage) : RearSuspension;

public sealed record LeverageRatioRearSuspension(LeverageRatio LeverageRatio) : RearSuspension;