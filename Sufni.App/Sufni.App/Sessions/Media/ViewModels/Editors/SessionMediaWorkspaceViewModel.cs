using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Shared.Common;

using Sufni.App.MapsAndTracks.Models;
using Sufni.App.MapsAndTracks.ViewModels;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Video.ViewModels;
namespace Sufni.App.Sessions.Media.ViewModels.Editors;

internal sealed class SessionMediaWorkspaceViewModel : ObservableObject, ISessionMediaWorkspace, IDisposable
{
    private readonly VideoPaneViewModel? videoPane;
    private readonly SegmentComparisonViewModel segmentComparison;
    private readonly IDisposable stateSubscription;
    private SurfacePresentationState mapState = SurfacePresentationState.Hidden;

    public SessionMediaWorkspaceViewModel(
        IObservable<RecordedSessionEditorState> state,
        MapViewModel? mapViewModel,
        SessionTimelineLinkViewModel timeline,
        VideoPaneViewModel? videoPane,
        SegmentComparisonViewModel segmentComparison)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeline);

        MapViewModel = mapViewModel;
        Timeline = timeline;
        this.videoPane = videoPane;
        if (videoPane is not null)
        {
            videoPane.PropertyChanged += OnVideoPanePropertyChanged;
        }

        this.segmentComparison = segmentComparison;
        segmentComparison.PropertyChanged += OnSegmentComparisonPropertyChanged;
        stateSubscription = state.Subscribe(ApplyState);
    }

    public bool HasMediaContent => MapState.ReservesLayout || Video is not null;

    public MapViewModel? MapViewModel { get; }

    public SurfacePresentationState MapState => mapState;

    public SessionTimelineLinkViewModel Timeline { get; }


    public VideoPaneViewModel? Video => videoPane is { HasVideo: true } ? videoPane : null;

    public MatchedTrackOverlay? MatchedTrack => segmentComparison.AnalysisComparison is { } analysis
        ? new MatchedTrackOverlay(analysis.Detail.MatchedSessionTrackPoints, analysis.Comparison.MatchedSegmentTrackPoints)
        : null;

    private void ApplyState(RecordedSessionEditorState state)
    {
        if (SetProperty(ref mapState, state.Presentation.MapState, nameof(MapState)))
        {
            OnPropertyChanged(nameof(HasMediaContent));
        }

    }

    private void OnVideoPanePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(VideoPaneViewModel.HasVideo))
        {
            OnPropertyChanged(nameof(Video));
            OnPropertyChanged(nameof(HasMediaContent));
        }
    }

    private void OnSegmentComparisonPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SegmentComparisonViewModel.AnalysisComparison))
        {
            OnPropertyChanged(nameof(MatchedTrack));
        }
    }

    public void Dispose()
    {
        stateSubscription.Dispose();
        segmentComparison.PropertyChanged -= OnSegmentComparisonPropertyChanged;
        if (videoPane is not null)
        {
            videoPane.PropertyChanged -= OnVideoPanePropertyChanged;
        }
    }
}
