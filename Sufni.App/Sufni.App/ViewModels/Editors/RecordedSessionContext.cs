using System.Collections.Generic;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Models;
using Sufni.App.ExtensionHost.Presentation;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Presentation;
using Sufni.App.SessionDetails;
using Sufni.App.Stores;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public sealed partial class RecordedSessionContext : ObservableObject
{
    public ObservableCollection<PageViewModelBase> Pages { get; } = [];

    public SessionTimelineLinkViewModel Timeline { get; } = new();

    [ObservableProperty] private SessionSnapshot? sessionSnapshot;
    [ObservableProperty] private TelemetryData? telemetryData;
    [ObservableProperty] private TelemetryTimeRange? analysisRange;
    [ObservableProperty] private List<TrackPoint>? fullTrackPoints;
    [ObservableProperty] private List<TrackPoint>? trackPoints;
    [ObservableProperty] private TrackTimeRange? trackTimelineContext;
    [ObservableProperty] private RecordedSessionExtensionSlots extensionSlots = new();
    [ObservableProperty] private SessionScreenPresentationState screenState = SessionScreenPresentationState.Ready;
    [ObservableProperty] private SessionOperationPresentationState sessionOperationState = SessionOperationPresentationState.Hidden;
}
