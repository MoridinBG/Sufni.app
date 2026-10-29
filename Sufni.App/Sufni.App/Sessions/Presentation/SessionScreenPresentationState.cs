using System;

namespace Sufni.App.Sessions.Presentation;

public enum SessionScreenStateKind
{
    Loading,
    Ready,
    IncompleteLocalData,
    Error,
}

public sealed record SessionScreenPresentationState
{
    public SessionScreenPresentationState(
        SessionScreenStateKind kind,
        string? message,
        double progressFraction)
    {
        if (kind == SessionScreenStateKind.Loading && string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException("Loading session screen state requires a message.", nameof(message));
        }

        Kind = kind;
        Message = message;
        ProgressFraction = kind == SessionScreenStateKind.Loading
            ? NormalizeProgress(progressFraction)
            : 0;
    }

    public SessionScreenStateKind Kind { get; }
    public string? Message { get; }
    public double ProgressFraction { get; }

    public bool IsLoading => Kind == SessionScreenStateKind.Loading;
    public bool IsReady => Kind == SessionScreenStateKind.Ready;
    public bool IsIncompleteLocalData => Kind == SessionScreenStateKind.IncompleteLocalData;
    public bool IsError => Kind == SessionScreenStateKind.Error;

    public static SessionScreenPresentationState Ready { get; } =
        new(SessionScreenStateKind.Ready, null, 0);

    public static SessionScreenPresentationState Loading(string message, double progressFraction)
    {
        return new SessionScreenPresentationState(
            SessionScreenStateKind.Loading,
            message,
            progressFraction);
    }

    public static SessionScreenPresentationState Error(string? message)
    {
        return new SessionScreenPresentationState(SessionScreenStateKind.Error, message, 0);
    }

    public static SessionScreenPresentationState IncompleteLocalData(string? message)
    {
        return new SessionScreenPresentationState(
            SessionScreenStateKind.IncompleteLocalData,
            message,
            0);
    }

    private static double NormalizeProgress(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }
}
