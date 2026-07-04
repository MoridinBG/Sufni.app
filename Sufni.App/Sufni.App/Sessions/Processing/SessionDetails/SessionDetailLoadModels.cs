using System;
using System.Collections.Generic;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;

using Sufni.App.Sessions.Models;
namespace Sufni.App.Sessions.Processing.SessionDetails;

public sealed record DampingSpeedCutoffOwner(Guid BikeId, long BaselineUpdated);

public sealed record SessionTelemetryPresentationData(
    TelemetryData TelemetryData,
    Guid? FullTrackId,
    List<TrackPoint>? FullTrackPoints,
    List<TrackPoint>? TrackPoints,
    double? MediaColumnWidth,
    SessionDampingPercentages DampingPercentages,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    DampingSpeedCutoffOwner? DampingSpeedCutoffOwner)
{
    public SessionTelemetryPresentationData(
        TelemetryData TelemetryData,
        Guid? FullTrackId,
        List<TrackPoint>? FullTrackPoints,
        List<TrackPoint>? TrackPoints,
        double? MediaColumnWidth,
        SessionDampingPercentages DampingPercentages)
        : this(
            TelemetryData,
            FullTrackId,
            FullTrackPoints,
            TrackPoints,
            MediaColumnWidth,
            DampingPercentages,
            DampingSpeedCutoffs.Default,
            null)
    {
    }
}

public sealed record SessionTrackPresentationData(
    Guid? FullTrackId,
    List<TrackPoint>? FullTrackPoints,
    List<TrackPoint>? TrackPoints,
    double? MediaColumnWidth);

public sealed record SessionCachePresentationData(
    string? FrontTravelDistribution,
    string? RearTravelDistribution,
    string? FrontVelocityDistribution,
    string? RearVelocityDistribution,
    string? CompressionBalance,
    string? ReboundBalance,
    SessionDampingPercentages DampingPercentages,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    bool BalanceAvailable,
    DampingSpeedCutoffOwner? DampingSpeedCutoffOwner = null)
{
    public SessionCachePresentationData(
        string? FrontTravelDistribution,
        string? RearTravelDistribution,
        string? FrontVelocityDistribution,
        string? RearVelocityDistribution,
        string? CompressionBalance,
        string? ReboundBalance,
        SessionDampingPercentages DampingPercentages,
        bool BalanceAvailable)
        : this(
            FrontTravelDistribution,
            RearTravelDistribution,
            FrontVelocityDistribution,
            RearVelocityDistribution,
            CompressionBalance,
            ReboundBalance,
            DampingPercentages,
            DampingSpeedCutoffs.Default,
            BalanceAvailable)
    {
    }

    public static SessionCachePresentationData FromCache(SessionCache cache)
    {
        var balanceAvailable = cache.CompressionBalance is not null && cache.ReboundBalance is not null;

        return new SessionCachePresentationData(
            cache.FrontTravelDistribution,
            cache.RearTravelDistribution,
            cache.FrontVelocityDistribution,
            cache.RearVelocityDistribution,
            cache.CompressionBalance,
            cache.ReboundBalance,
            cache.DampingPercentages,
            cache.DampingSpeedCutoffs,
            balanceAvailable);
    }

    public SessionCache ToCache(Guid sessionId)
    {
        return new SessionCache
        {
            SessionId = sessionId,
            FrontTravelDistribution = FrontTravelDistribution,
            RearTravelDistribution = RearTravelDistribution,
            FrontVelocityDistribution = FrontVelocityDistribution,
            RearVelocityDistribution = RearVelocityDistribution,
            CompressionBalance = BalanceAvailable ? CompressionBalance : null,
            ReboundBalance = BalanceAvailable ? ReboundBalance : null,
            DampingPercentages = this.DampingPercentages,
            DampingSpeedCutoffs = this.DampingSpeedCutoffs,
        };
    }
}

public readonly record struct SessionPresentationDimensions(int Width, int Height)
{
    public int TravelDistributionWidth => Math.Max(1, Width);
    public int TravelDistributionHeight => Math.Max(1, Height);
    public int VelocityDistributionWidth => Math.Max(1, Width - 64);
    public int VelocityDistributionHeight => 478;
}

public sealed record MissingSessionData(
    bool ProcessedTelemetryBlob,
    bool RecordedSourceMissingOrHashMismatch);

public abstract record SessionDesktopLoadResult
{
    private SessionDesktopLoadResult() { }

    public sealed record Loaded(SessionTelemetryPresentationData Data) : SessionDesktopLoadResult;
    public sealed record TelemetryPending : SessionDesktopLoadResult;
    public sealed record Failed(string ErrorMessage) : SessionDesktopLoadResult;
}

public abstract record SessionMobileLoadResult
{
    private SessionMobileLoadResult() { }

    public sealed record LoadedFromCache(SessionCachePresentationData Data, TelemetryData? Telemetry, SessionTrackPresentationData? TrackData) : SessionMobileLoadResult;
    public sealed record BuiltCache(SessionCachePresentationData Data, TelemetryData Telemetry, SessionTrackPresentationData TrackData) : SessionMobileLoadResult;
    public sealed record IncompleteLocalData(Guid SessionId, MissingSessionData Missing) : SessionMobileLoadResult;
    public sealed record TelemetryPending : SessionMobileLoadResult;
    public sealed record Failed(string ErrorMessage) : SessionMobileLoadResult;
}
