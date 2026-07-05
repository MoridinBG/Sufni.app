using System.Collections.Generic;
using System.Reactive.Subjects;
using Sufni.App.ExtensionHost.Contracts.Models;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.ExtensionHost.Runtime.Presentation;
using Sufni.App.Infrastructure;
using Sufni.App.MapsAndTracks.Models;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Processing.SessionDetails;
using Sufni.App.Sessions.Store;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

internal sealed class RecordedSessionEditorStateControllerTestDriver : IDisposable
{
    public RecordedSessionEditorStateControllerTestDriver()
    {
        Controller = new RecordedSessionEditorStateController(
            new RecordedSessionEditorStateInputs(
                Actions.Intents,
                PageCounts,
                PreferenceReplays,
                LoadPresentations,
                OperationStates,
                MediaPaneStates,
                MediaUrls,
                DampingPercentages,
                PlotDampingSpeedCutoffs,
                CanEditDampingSpeedCutoffs,
                SessionInsights,
                AnalysisSelections,
                SignalPlotContextMenuActions,
                DomainStates));
    }

    public RecordedSessionEditorActions Actions { get; } = new();
    public Subject<int> PageCounts { get; } = new();
    public Subject<SessionPreferences> PreferenceReplays { get; } = new();
    public Subject<RecordedSessionLoadPresentation> LoadPresentations { get; } = new();
    public Subject<SessionOperationPresentationState> OperationStates { get; } = new();
    public Subject<SurfacePresentationState> MediaPaneStates { get; } = new();
    public Subject<string?> MediaUrls { get; } = new();
    public Subject<SessionDampingPercentages> DampingPercentages { get; } = new();
    public Subject<DampingSpeedCutoffs> PlotDampingSpeedCutoffs { get; } = new();
    public Subject<bool> CanEditDampingSpeedCutoffs { get; } = new();
    public Subject<SessionInsightsResult> SessionInsights { get; } = new();
    public Subject<AnalysisSelectionState> AnalysisSelections { get; } = new();
    public Subject<IReadOnlyDictionary<string, IReadOnlyList<TelemetryPlotContextMenuAction>>> SignalPlotContextMenuActions { get; } = new();
    public Subject<RecordedSessionDomainSnapshot> DomainStates { get; } = new();
    public RecordedSessionEditorStateController Controller { get; }

    public void PublishTelemetry(TelemetryData telemetryData)
    {
        PublishLoadedData(telemetryData: telemetryData);
    }

    public void PublishLoadedData(
        SessionSnapshot? session = null,
        TelemetryData? telemetryData = null,
        IReadOnlyList<TrackPoint>? fullTrackPoints = null,
        IReadOnlyList<TrackPoint>? trackPoints = null,
        double? mediaColumnWidth = null)
    {
        if (telemetryData is null)
        {
            LoadPresentations.OnNext(new RecordedSessionLoadPresentation.Empty(session));
            return;
        }

        LoadPresentations.OnNext(new RecordedSessionLoadPresentation.Loaded(
            new SessionDetailData(
                new SessionTelemetryPresentationData(
                    telemetryData,
                    session?.FullTrackId,
                    fullTrackPoints is List<TrackPoint> fullTrackList ? fullTrackList : fullTrackPoints?.ToList(),
                    trackPoints is List<TrackPoint> trackList ? trackList : trackPoints?.ToList(),
                    mediaColumnWidth,
                    SessionDampingPercentages.Empty),
                new SessionCachePresentationData(
                    FrontTravelDistribution: null,
                    RearTravelDistribution: null,
                    FrontVelocityDistribution: null,
                    RearVelocityDistribution: null,
                    CompressionBalance: null,
                    ReboundBalance: null,
                    DampingPercentages: SessionDampingPercentages.Empty,
                    BalanceAvailable: false)),
            session));
    }

    public void Dispose()
    {
        Controller.Dispose();
        Actions.Dispose();
        PageCounts.Dispose();
        PreferenceReplays.Dispose();
        LoadPresentations.Dispose();
        OperationStates.Dispose();
        MediaPaneStates.Dispose();
        MediaUrls.Dispose();
        DampingPercentages.Dispose();
        PlotDampingSpeedCutoffs.Dispose();
        CanEditDampingSpeedCutoffs.Dispose();
        SessionInsights.Dispose();
        AnalysisSelections.Dispose();
        SignalPlotContextMenuActions.Dispose();
        DomainStates.Dispose();
    }
}
