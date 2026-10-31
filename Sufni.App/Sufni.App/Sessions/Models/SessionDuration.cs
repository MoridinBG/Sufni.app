using System;

namespace Sufni.App.Sessions.Models;

internal static class SessionDuration
{
    public static double? Normalize(double? value) =>
        value is { } duration && double.IsFinite(duration) && duration >= 0 ? duration : null;
}
