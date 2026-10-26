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
        Action<string> reportError);
}

internal sealed class SegmentPickerFactory(
    ISessionSegmentStore segmentStore,
    ISegmentCoordinator segmentCoordinator,
    IDialogService dialogService) : ISegmentPickerFactory
{
    public SegmentPickerViewModel Create(
        Guid sessionId,
        IObservable<SegmentPickerSessionState> sessionState,
        Action<TelemetryTimeRange?> setAnalysisRange,
        Action<string> reportError) =>
        new(sessionId, sessionState, segmentStore, segmentCoordinator, dialogService, setAnalysisRange, reportError);
}
