using System;
using System.Collections.Generic;
using Sufni.App.Models;
using Sufni.Telemetry;

namespace Sufni.App.SessionDetails;

public sealed record SessionTelemetryPresentationData(
    TelemetryData TelemetryData,
    Guid? FullTrackId,
    List<TrackPoint>? FullTrackPoints,
    List<TrackPoint>? TrackPoints,
    double? MapVideoWidth,
    SessionDamperPercentages DamperPercentages);

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
    bool BalanceAvailable)
{
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
            new SessionDamperPercentages(
                cache.FrontHscPercentage,
                cache.RearHscPercentage,
                cache.FrontLscPercentage,
                cache.RearLscPercentage,
                cache.FrontLsrPercentage,
                cache.RearLsrPercentage,
                cache.FrontHsrPercentage,
                cache.RearHsrPercentage),
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
            FrontHscPercentage = DamperPercentages.FrontHscPercentage,
            RearHscPercentage = DamperPercentages.RearHscPercentage,
            FrontLscPercentage = DamperPercentages.FrontLscPercentage,
            RearLscPercentage = DamperPercentages.RearLscPercentage,
            FrontLsrPercentage = DamperPercentages.FrontLsrPercentage,
            RearLsrPercentage = DamperPercentages.RearLsrPercentage,
            FrontHsrPercentage = DamperPercentages.FrontHsrPercentage,
            RearHsrPercentage = DamperPercentages.RearHsrPercentage,
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

    public sealed record LoadedFromCache(SessionCachePresentationData Data, TelemetryData? Telemetry) : SessionMobileLoadResult;
    public sealed record BuiltCache(SessionCachePresentationData Data, TelemetryData Telemetry) : SessionMobileLoadResult;
    public sealed record TelemetryPending : SessionMobileLoadResult;
    public sealed record Failed(string ErrorMessage) : SessionMobileLoadResult;
}