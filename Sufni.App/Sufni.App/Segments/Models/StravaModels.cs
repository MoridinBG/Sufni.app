using System.Collections.Generic;
using Sufni.Telemetry;

namespace Sufni.App.Segments.Models;

public sealed record StravaConnectionSnapshot(
    string? ClientId,
    string? ClientSecret,
    bool IsConnected);

public sealed record StravaConnectRequest(
    string? ClientId,
    string? ClientSecret,
    string? RedirectUrl);

public abstract record StravaConnectResult
{
    private StravaConnectResult() { }

    public sealed record Connected : StravaConnectResult;
    public sealed record ValidationFailed(string Message) : StravaConnectResult;
    public sealed record MissingRequiredScope : StravaConnectResult;
    public sealed record RateLimited : StravaConnectResult;
    public sealed record Failed(string Message) : StravaConnectResult;
}

public abstract record StravaSegmentFetchResult
{
    private StravaSegmentFetchResult() { }

    public sealed record Loaded(IReadOnlyList<StravaSegmentOption> Segments) : StravaSegmentFetchResult;
    public sealed record NotConnected : StravaSegmentFetchResult;
    public sealed record MissingSessionTiming : StravaSegmentFetchResult;
    public sealed record ReconnectRequired : StravaSegmentFetchResult;
    public sealed record NoMatchingActivity : StravaSegmentFetchResult;
    public sealed record NoUsableSegments : StravaSegmentFetchResult;
    public sealed record RateLimited : StravaSegmentFetchResult;
    public sealed record Failed(string Message) : StravaSegmentFetchResult;
}

/// <summary>
/// One Strava segment effort of the overlapping activity, as a range of the
/// session's telemetry clamped to its duration.
/// </summary>
public sealed record StravaSegmentOption(
    string Name,
    double DistanceMeters,
    TelemetryTimeRange AnalysisRange);
