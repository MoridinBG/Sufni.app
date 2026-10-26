using System;
using Sufni.Telemetry;

using Sufni.App.Infrastructure;
using Sufni.App.Segments.Coordinators;
using Sufni.App.Segments.Services;
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
    SegmentPickerViewModel Create(Guid sessionId, Action<TelemetryTimeRange?> setAnalysisRange, Action<string> reportError);
}

internal sealed class SegmentPickerFactory(
    ISessionSegmentStore segmentStore,
    ISegmentCoordinator segmentCoordinator,
    IStravaSegmentService stravaService,
    IDialogService dialogService) : ISegmentPickerFactory
{
    public SegmentPickerViewModel Create(Guid sessionId, Action<TelemetryTimeRange?> setAnalysisRange, Action<string> reportError) =>
        new(sessionId, segmentStore, segmentCoordinator, stravaService, dialogService, setAnalysisRange, reportError);
}
