using System;

namespace Sufni.App.Presentation;

public sealed record SessionOperationPresentationState(
    bool IsVisible,
    string? Message,
    double Percent)
{
    public double ProgressFraction => Math.Clamp(Percent, 0, 100) / 100.0;

    public static SessionOperationPresentationState Hidden { get; } = new(false, null, 0);

    public static SessionOperationPresentationState Progress(string message, double percent)
    {
        return new SessionOperationPresentationState(
            true,
            message,
            Math.Clamp(percent, 0, 100));
    }
}
