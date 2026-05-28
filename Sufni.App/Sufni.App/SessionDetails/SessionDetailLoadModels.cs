using System;
using System.Collections.Generic;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.SessionDetails;

public sealed record DampingSpeedCutoffOwner(Guid BikeId, long BaselineUpdated);

public sealed record SessionTelemetryPresentationData(
    TelemetryData TelemetryData,
    Guid? FullTrackId,
    List<TrackPoint>? FullTrackPoints,
    List<TrackPoint>? TrackPoints,
    double? MapVideoWidth,
    SessionDamperPercentages DamperPercentages,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    DampingSpeedCutoffOwner? DampingSpeedCutoffOwner)
{
    public SessionTelemetryPresentationData(
        TelemetryData TelemetryData,
        Guid? FullTrackId,
        List<TrackPoint>? FullTrackPoints,
        List<TrackPoint>? TrackPoints,
        double? MapVideoWidth,
        SessionDamperPercentages DamperPercentages)
        : this(
            TelemetryData,
            FullTrackId,
            FullTrackPoints,
            TrackPoints,
            MapVideoWidth,
            DamperPercentages,
            DampingSpeedCutoffs.Default,
            null)
    {
    }
}

public sealed record SessionTrackPresentationData(
    Guid? FullTrackId,
    List<TrackPoint>? FullTrackPoints,
    List<TrackPoint>? TrackPoints,
    double? MapVideoWidth);

public sealed record SessionCachePresentationData(
    string? FrontTravelHistogram,
    string? RearTravelHistogram,
    string? FrontVelocityHistogram,
    string? RearVelocityHistogram,
    string? CompressionBalance,
    string? ReboundBalance,
    SessionDamperPercentages DamperPercentages,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    bool BalanceAvailable,
    DampingSpeedCutoffOwner? DampingSpeedCutoffOwner = null)
{
    public SessionCachePresentationData(
        string? FrontTravelHistogram,
        string? RearTravelHistogram,
        string? FrontVelocityHistogram,
        string? RearVelocityHistogram,
        string? CompressionBalance,
        string? ReboundBalance,
        SessionDamperPercentages DamperPercentages,
        bool BalanceAvailable)
        : this(
            FrontTravelHistogram,
            RearTravelHistogram,
            FrontVelocityHistogram,
            RearVelocityHistogram,
            CompressionBalance,
            ReboundBalance,
            DamperPercentages,
            DampingSpeedCutoffs.Default,
            BalanceAvailable)
    {
    }

    public static SessionCachePresentationData FromCache(SessionCache cache)
    {
        var balanceAvailable = cache.CompressionBalance is not null && cache.ReboundBalance is not null;

        return new SessionCachePresentationData(
            cache.FrontTravelHistogram,
            cache.RearTravelHistogram,
            cache.FrontVelocityHistogram,
            cache.RearVelocityHistogram,
            cache.CompressionBalance,
            cache.ReboundBalance,
            cache.DamperPercentages,
            cache.DampingSpeedCutoffs,
            balanceAvailable);
    }

    public SessionCache ToCache(Guid sessionId)
    {
        return new SessionCache
        {
            SessionId = sessionId,
            FrontTravelHistogram = FrontTravelHistogram,
            RearTravelHistogram = RearTravelHistogram,
            FrontVelocityHistogram = FrontVelocityHistogram,
            RearVelocityHistogram = RearVelocityHistogram,
            CompressionBalance = BalanceAvailable ? CompressionBalance : null,
            ReboundBalance = BalanceAvailable ? ReboundBalance : null,
            DamperPercentages = this.DamperPercentages,
            DampingSpeedCutoffs = this.DampingSpeedCutoffs,
        };
    }
}

public readonly record struct SessionPresentationDimensions(int Width, int Height)
{
    public int TravelHistogramWidth => Math.Max(1, Width);
    public int TravelHistogramHeight => Math.Max(1, Height);
    public int VelocityHistogramWidth => Math.Max(1, Width - 64);
    public int VelocityHistogramHeight => 478;
}

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
    public sealed record TelemetryPending : SessionMobileLoadResult;
    public sealed record Failed(string ErrorMessage) : SessionMobileLoadResult;
}
