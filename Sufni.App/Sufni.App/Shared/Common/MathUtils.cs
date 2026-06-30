using System;

namespace Sufni.App.Shared.Common;

public static class MathUtils
{
    public static bool AreEqual(double? a, double? b, double epsilon = 1e-3)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return Math.Abs(a.Value - b.Value) < epsilon;
    }
}