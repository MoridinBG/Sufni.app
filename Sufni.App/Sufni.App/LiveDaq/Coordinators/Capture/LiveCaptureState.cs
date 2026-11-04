using System;
using System.Collections.Generic;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.MapsAndTracks.Models;

namespace Sufni.App.LiveDaq.Coordinators.Capture;

public enum LiveCapturePhase { Active, AwaitingReset, Closed }

public readonly record struct LiveSignalPresence(bool HasTravelData, bool HasImuData, bool HasPitchRollData);

public sealed record LiveCaptureControls(
    LiveConnectionState ConnectionState,
    string? LastError,
    LiveSessionHeader? SessionHeader,
    DateTimeOffset? CaptureStartUtc,
    TimeSpan CaptureDuration,
    bool CanSave,
    LiveCapturePhase Phase = LiveCapturePhase.Active)
{
    public static readonly LiveCaptureControls Empty = new(
        new LiveConnectionState.Disconnected(), null, null, null, TimeSpan.Zero, false);
}

/// <summary>In-memory capture facts; their lifetime ends with the tab or process.</summary>
public sealed record LiveCaptureState(
    long Revision,
    long CaptureRevision,
    LiveCaptureControls Controls,
    IReadOnlyList<TrackPoint> TrackPoints,
    LiveSignalPresence SignalPresence,
    LiveSessionAnalysis? Analysis,
    LiveAnalysisFailure? AnalysisFailure)
{
    public static readonly LiveCaptureState Empty = new(0, 0, LiveCaptureControls.Empty, [], default, null, null);
}
