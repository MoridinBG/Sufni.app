using System;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;

namespace Sufni.App.Sessions.Coordination;

public enum SessionWindowEditKind
{
    TrimFromBeginning,
    TrimUntilEnd,
    Split,
    UndoTrim
}

/// <summary>Pure window, timeline and sample-count rules for recorded-session edits.</summary>
public static class SessionWindowEditPolicy
{
    public static bool CanEditAt(SessionWindowEditKind kind, double clickSeconds, double durationSeconds, double sampleRate)
    {
        if (!double.IsFinite(clickSeconds) || !double.IsFinite(durationSeconds) || sampleRate <= 0)
            return false;
        var (before, after) = kind switch
        {
            SessionWindowEditKind.TrimFromBeginning => (1, 5),
            SessionWindowEditKind.TrimUntilEnd => (5, 1),
            SessionWindowEditKind.Split => (5, 5),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        return clickSeconds * sampleRate >= before && (durationSeconds - clickSeconds) * sampleRate >= after;
    }

    public static SessionWindowEditPlan Plan(SessionSnapshot session, SessionWindowEditKind kind, double clickSeconds = 0)
    {
        var window = session.DerivationWindow ?? new RecordedSessionDerivationWindow(session.Id, 0, null);
        if (kind == SessionWindowEditKind.UndoTrim)
        {
            if (session.PreviousWindowStartSeconds is not { } previousStart)
                return new SessionWindowEditPlan.Rejected("Session has no trim to undo.");
            var restored = window with { StartSeconds = previousStart, EndSeconds = session.PreviousWindowEndSeconds };
            return new SessionWindowEditPlan.Planned(
                Patch(restored, null, session.Name, moveOrigin: true), null);
        }

        var split = window.StartSeconds + clickSeconds;
        if (!double.IsFinite(split) || split <= window.StartSeconds || split >= (window.EndSeconds ?? double.PositiveInfinity))
            return new SessionWindowEditPlan.Rejected("The position is outside the session.");

        return kind switch
        {
            SessionWindowEditKind.TrimUntilEnd => new SessionWindowEditPlan.Planned(
                Patch(window with { EndSeconds = split }, window, session.Name), null),
            SessionWindowEditKind.TrimFromBeginning => new SessionWindowEditPlan.Planned(
                Patch(window with { StartSeconds = split }, window, session.Name, moveOrigin: true), null),
            SessionWindowEditKind.Split => new SessionWindowEditPlan.Planned(
                Patch(window with { EndSeconds = split }, null, $"{session.Name} (1)"),
                Patch(window with { StartSeconds = split }, null, $"{session.Name} (2)", moveOrigin: true, created: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        SessionWindowPatch Patch(RecordedSessionDerivationWindow next, RecordedSessionDerivationWindow? previous,
            string name, bool moveOrigin = false, bool created = false)
        {
            var timestamp = session.Timestamp;
            var offset = session.GpsOffsetSeconds;
            if (moveOrigin)
            {
                if (timestamp is { } origin)
                {
                    timestamp = origin - (long)Math.Floor(window.StartSeconds) + (long)Math.Floor(next.StartSeconds);
                    offset += FractionDelta(window.StartSeconds, next.StartSeconds);
                }
                else offset = 0;
            }
            var fullOwnRange = !created && next.SourceSessionId == session.Id && next.StartSeconds == 0 && next.EndSeconds is null;
            return new SessionWindowPatch(name, timestamp, offset, fullOwnRange ? null : next, previous);
        }
    }

    // Whole seconds live in Timestamp; GPS still carries the fractional origin until its migration.
    private static double FractionDelta(double from, double to) =>
        to - Math.Floor(to) - (from - Math.Floor(from));
}

public sealed record SessionWindowPatch(string Name, long? Timestamp, double GpsOffsetSeconds,
    RecordedSessionDerivationWindow? Window, RecordedSessionDerivationWindow? PreviousWindow);

public abstract record SessionWindowEditPlan
{
    private SessionWindowEditPlan() { }

    public sealed record Rejected(string Message) : SessionWindowEditPlan;
    public sealed record Planned(SessionWindowPatch Current, SessionWindowPatch? Created) : SessionWindowEditPlan;
}
