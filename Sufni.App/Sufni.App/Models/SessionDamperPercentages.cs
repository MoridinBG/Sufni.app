namespace Sufni.App.Models;

public sealed record SessionDamperPercentages(
    double? FrontHscPercentage,
    double? RearHscPercentage,
    double? FrontLscPercentage,
    double? RearLscPercentage,
    double? FrontLsrPercentage,
    double? RearLsrPercentage,
    double? FrontHsrPercentage,
    double? RearHsrPercentage);