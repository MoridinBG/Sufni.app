using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Stores;
using Sufni.App.Shared.Models;
using System;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Stores;
namespace Sufni.App.Segments.ViewModels;

/// <summary>
/// Creates the segments picker of a session editor.
/// </summary>
public interface ISegmentPickerFactory
{
    /// <summary>
    /// <paramref name="setAnalysisRange"/> receives null to clear the analysis range.
    /// </summary>
    SegmentPickerViewModel Create(
        Guid sessionId,
        IObservable<SegmentPickerSessionState> sessionState,
        Action<TelemetryTimeRange?> setAnalysisRange,
        Action<FailurePresentation> reportError,
        Action<string> reportNotification);
}

internal sealed class SegmentPickerFactory(
    ISessionSegmentStore segmentStore,
    ISegmentCoordinator segmentCoordinator,
    ITerrainLabelStore labelStore,
    ITerrainLabelCoordinator labelCoordinator,
    IDialogService dialogService) : ISegmentPickerFactory
{
    public SegmentPickerViewModel Create(
        Guid sessionId,
        IObservable<SegmentPickerSessionState> sessionState,
        Action<TelemetryTimeRange?> setAnalysisRange,
        Action<FailurePresentation> reportError,
        Action<string> reportNotification) =>
        new(sessionId, sessionState, segmentStore, segmentCoordinator, labelStore, labelCoordinator, dialogService, setAnalysisRange,
            reportError, reportNotification);
}
