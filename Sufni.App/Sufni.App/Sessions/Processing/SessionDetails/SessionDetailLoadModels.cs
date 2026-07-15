using System;
using System.Collections.Generic;
using Sufni.Telemetry;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
namespace Sufni.App.Sessions.Processing.SessionDetails;

public sealed record DampingSpeedCutoffOwner(Guid BikeId, long BaselineUpdated);

public enum SessionDetailLoadStage
{
    PreparingSession,
    LoadingTelemetryData,
    CheckingLocalData,
    LoadingMapData,
    BuildingSessionPresentation,
    FinalizingSessionData,
    ApplyingSessionData,
}

public sealed record SessionDetailLoadProgress(
    SessionDetailLoadStage Stage,
    string Message,
    double ProgressFraction)
{
    public static SessionDetailLoadProgress PreparingSession { get; } =
        new(SessionDetailLoadStage.PreparingSession, "Preparing session...", 0.05);

    public static SessionDetailLoadProgress LoadingTelemetryData { get; } =
        new(SessionDetailLoadStage.LoadingTelemetryData, "Loading telemetry data...", 0.20);

    public static SessionDetailLoadProgress CheckingLocalData { get; } =
        new(SessionDetailLoadStage.CheckingLocalData, "Checking local data...", 0.35);

    public static SessionDetailLoadProgress LoadingMapData { get; } =
        new(SessionDetailLoadStage.LoadingMapData, "Loading map data...", 0.50);

    public static SessionDetailLoadProgress BuildingSessionPresentation { get; } =
        new(SessionDetailLoadStage.BuildingSessionPresentation, "Building session presentation...", 0.70);

    public static SessionDetailLoadProgress FinalizingSessionData { get; } =
        new(SessionDetailLoadStage.FinalizingSessionData, "Finalizing session data...", 0.90);

    public static SessionDetailLoadProgress ApplyingSessionData { get; } =
        new(SessionDetailLoadStage.ApplyingSessionData, "Applying session data...", 0.95);
}

public sealed record SessionTelemetryPresentationData(
    TelemetryData TelemetryData,
    Guid? FullTrackId,
    List<TrackPoint>? FullTrackPoints,
    List<TrackPoint>? TrackPoints,
    double? MediaColumnWidth,
    DampingSpeedCutoffs DampingSpeedCutoffs,
    DampingSpeedCutoffOwner? DampingSpeedCutoffOwner)
{
    public SessionTelemetryPresentationData(
        TelemetryData TelemetryData,
        Guid? FullTrackId,
        List<TrackPoint>? FullTrackPoints,
        List<TrackPoint>? TrackPoints,
        double? MediaColumnWidth)
        : this(
            TelemetryData,
            FullTrackId,
            FullTrackPoints,
            TrackPoints,
            MediaColumnWidth,
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

}

public readonly record struct SessionPresentationDimensions(int Width, int Height)
{
    public static SessionPresentationDimensions Default { get; } = new(320, 180);

    public int TravelDistributionWidth => Math.Max(1, Width);
    public int TravelDistributionHeight => Math.Max(1, Height);
    public int VelocityDistributionWidth => Math.Max(1, Width - 64);
    public int VelocityDistributionHeight => 478;
}

public sealed record MissingSessionData(
    bool ProcessedTelemetryBlob,
    bool RecordedSourceMissingOrHashMismatch);

public sealed record SessionDetailData(SessionTelemetryPresentationData TelemetryPresentation);

public abstract record SessionDetailLoadResult
{
    private SessionDetailLoadResult() { }

    public sealed record Loaded(SessionDetailData Data) : SessionDetailLoadResult;
    public sealed record IncompleteLocalData(Guid SessionId, MissingSessionData Missing) : SessionDetailLoadResult;
    public sealed record Failed(string ErrorMessage) : SessionDetailLoadResult;
}
