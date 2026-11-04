using System.Reactive.Subjects;
using NSubstitute;
using Sufni.App.Segments.ViewModels;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Signals.ViewModels.Editors;
using Sufni.App.Shared.Models;
using Sufni.App.Shared.Views.Plots;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Video.ViewModels;

namespace Sufni.App.Tests.TestSupport.Sessions;

internal sealed class SessionPlotActionsFixture : IDisposable
{
    public Guid SessionId { get; } = Guid.NewGuid();
    public BehaviorSubject<RecordedSessionEditorState> State { get; } = new(RecordedSessionEditorState.CreateInitial());
    public RecordedSessionEditorActions EditorActions { get; } = new();
    public CancellationTokenSource Lifetime { get; } = new();
    public ISessionCoordinator SessionCoordinator { get; } = TestCoordinatorSubstitutes.Session();
    public ISessionEditingCoordinator EditingCoordinator { get; } = Substitute.For<ISessionEditingCoordinator>();
    public IShellCoordinator Shell { get; } = Substitute.For<IShellCoordinator>();
    public List<FailurePresentation> Errors { get; } = [];
    public List<string> Notifications { get; } = [];
    public SegmentPickerViewModel Picker { get; } = TestSegmentPickers.Create();
    public SessionTimelineLinkViewModel Timeline { get; } = new();
    public TimelineAlignmentController Alignment { get; }
    public SessionWindowEditController WindowEdits { get; }
    public SignalPlotContextActions ContextActions { get; }

    public SessionPlotActionsFixture(VideoPaneViewModel? video = null,
        IObservable<RecordedSessionEditorState>? state = null, RecordedSessionEditorActions? actions = null)
    {
        State.OnNext(State.Value with
        {
            Session = TestSnapshots.Session(id: SessionId, hasProcessedData: true),
            TelemetryData = TestTelemetryData.CreateMinimal(duration: 10, sampleRate: 1000),
            TrackPoints = [new(1, 0, 0, 0)],
        });
        var states = state ?? State;
        Alignment = new(SessionId, states, SessionCoordinator, video, Lifetime.Token, Errors.Add, Notifications.Add);
        WindowEdits = new(SessionId, states, EditingCoordinator, Shell, new InlineUiThreadDispatcher(), Lifetime.Token, Errors.Add, Notifications.Add);
        ContextActions = new(states, actions ?? EditorActions, Timeline, Picker, Alignment, WindowEdits, Lifetime.Token);
    }

    public static TelemetryPlotContextMenuContext Context(double seconds) => new("travel", seconds, 10, null);

    public void Dispose()
    {
        Lifetime.Cancel();
        ContextActions.Dispose();
        Alignment.Dispose();
        WindowEdits.Dispose();
        Picker.Dispose();
        EditorActions.Dispose();
        State.Dispose();
        Lifetime.Dispose();
    }
}
