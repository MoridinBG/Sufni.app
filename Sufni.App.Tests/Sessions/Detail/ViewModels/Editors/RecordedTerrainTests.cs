using Sufni.App.Tests.TestSupport.Async;
using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Coordinators;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Sessions.Coordination;
using Sufni.Telemetry.Tests;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Common;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

[Collection("Ui")]
public class RecordedTerrainTests
{
    [AvaloniaFact]
    public async Task RoughEntryNarrowsTheDampingShareToTheRoughSectionAndResetRestoresTheDefault()
    {
        using var h = new SessionDetailHarness();
        var ride = SyntheticRide.Build([new(140, LowBandG: 0.2, MiddleBandG: 0.2, HighBandG: 0.2),
            new(140, LowBandG: 1.5, MiddleBandG: 1.6, HighBandG: 1.8)]);
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, ride.Profile(), TestSnapshots.TerrainScale(SyntheticRide.Scale));
        var editor = h.CreateRecordedEditor(session, h.CreateLoadedResult(ride.Telemetry), Observable.Return(domain));
        await h.LoadAsync(editor);
        var scope = editor.SignalsWorkspace.TerrainScope!;
        var rough = scope.Roughness[(int)RoughnessClass.Rough];
        Assert.InRange(rough.DistanceMetres, 120, 150);
        var analysis = editor.AnalysisWorkspace;
        var full = analysis.DampingPercentages;
        rough.Select.Execute(null);
        Assert.Equal(rough.DistanceMetres, scope.SelectedMetres);
        Assert.True(analysis.DampingPercentages.FrontHscPercentage > full.FrontHscPercentage);
        scope.Reset.Execute(null);
        Assert.True(scope.IsDefault);
        Assert.Equal(full, analysis.DampingPercentages);
        await h.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(TerrainGradeState.Available, "Descents", 20)]
    [InlineData(TerrainGradeState.NoElevation, "All", 30)]
    public async Task DefaultScopeFollowsTheProfileGradeAndLeavesOutStoppedTime(TerrainGradeState grade, string label, double metres)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var flat = TerrainProfiles.Window(0, RoughnessClass.Calm);
        var profile = TerrainProfiles.Profile([flat, TerrainProfiles.Window(1, moving: false),
            TerrainProfiles.Window(2, RoughnessClass.Calm),
            TerrainProfiles.Window(3, RoughnessClass.SmoothClimb) with { Band = GradeBand.Climb }], grade: grade);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, profile)));
        await h.LoadAsync(editor);
        var scope = editor.AnalysisWorkspace.TerrainScope!;
        Assert.True(scope.IsDefault);
        Assert.Equal(label, scope.Label);
        Assert.Equal(metres, scope.SelectedMetres);
        Assert.Equal(grade == TerrainGradeState.Available, scope.Presets[(int)TerrainPreset.Climbs].IsEnabled);
        Assert.Equal(grade != TerrainGradeState.Available, scope.Note is not null);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ChosenPresetRefinesWithItsOwnClassesAndTheDefaultPresetKeepsTheDefault()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Rough),
            TerrainProfiles.Window(1, RoughnessClass.TechnicalClimb) with { Band = GradeBand.SteepClimb }]);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, profile)));
        await h.LoadAsync(editor);
        var scope = editor.AnalysisWorkspace.TerrainScope!;
        Assert.False(scope.Roughness[(int)RoughnessClass.TechnicalClimb].IsEnabled);
        scope.Presets[(int)TerrainPreset.Climbs].Select.Execute(null);
        Assert.False(scope.IsDefault);
        Assert.Equal(10, scope.SelectedMetres);
        Assert.False(scope.Roughness[(int)RoughnessClass.Rough].IsEnabled);
        scope.Roughness[(int)RoughnessClass.TechnicalClimb].Select.Execute(null);
        Assert.Equal("Climbs · Technical climb", scope.Label);
        scope.Presets[(int)TerrainPreset.Descents].Select.Execute(null);
        Assert.True(scope.IsDefault);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ChosenScopeAndComparisonReplaceEachOtherAndNewTelemetryRestoresTheDefault()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var compared = SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), TestSegmentMatches.Telemetry(), 30)!;
        TestSegmentComparisons.DetailReturns(h.SegmentCoordinator, compared);
        var editor = await h.LoadMatchedEditorAsync(session, changes, compared);
        var signals = editor.SignalsWorkspace;
        var scope = signals.TerrainScope!;
        var entry = scope.Roughness[(int)RoughnessClass.Rough];
        entry.Select.Execute(null);
        Assert.False(scope.IsDefault);
        var comparisons = signals.SegmentComparison;
        var row = Assert.Single(comparisons.TravelRows);
        await ((IAsyncRelayCommand)row.HeaderActions.Single(a => a.Command is IAsyncRelayCommand).Command!).ExecuteAsync(null);
        Assert.True(comparisons.HasAnalysisComparison);
        Assert.False(scope.Available);
        entry.Select.Execute(null);
        Assert.True(scope.Available);
        comparisons.ClearAnalysisComparisonCommand.Execute(null);
        Assert.True(entry.IsChecked);
        var replacement = session with { ProcessedTelemetryRevision = session.ProcessedTelemetryRevision + 1 };
        h.ConfigureLoad(replacement, h.CreateLoadedResult());
        changes.OnNext(SessionDetailHarness.DomainFromSnapshot(replacement));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(entry.IsChecked);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task GpsOffsetEditReleasesTheChosenScope()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var scope = editor.SignalsWorkspace.TerrainScope!;
        scope.Roughness[(int)RoughnessClass.Rough].Select.Execute(null);
        Assert.False(scope.IsDefault);
        changes.OnNext(domain with { Session = session with { GpsOffsetSeconds = 2 }, Terrain = new TerrainStatus.Pending() });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(scope.IsDefault);
        await h.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StripBreaksAtStopsAndReportsEachSourceShare(bool forkClipped)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var clipped = TerrainProfiles.Window(3, RoughnessClass.Rough) with
        {
            Fork =forkClipped ? TerrainProfiles.Sensor(TerrainProfiles.Level(RoughnessClass.VeryRough), clippedShare: 0.12) : null
        };
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Calm, GroundSensors.Fork),
            TerrainProfiles.Window(1, RoughnessClass.Rough, GroundSensors.Fork),
            TerrainProfiles.Window(2, RoughnessClass.Rough) with { Moving = false, Grade = null }, clipped,
            TerrainProfiles.Window(4, RoughnessClass.Rough), TerrainProfiles.Window(5, RoughnessClass.Rough, grade: 0.3)]) with
        {
            GradePieces = [TerrainProfiles.Piece(3, 6) with { Grade = 0.1, Band = GradeBand.Flat }]
        };
        var domain = h.WithTerrain(session, profile);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(domain));
        await h.LoadAsync(editor);
        var presentation = Assert.IsType<TerrainPresentation>(editor.SignalsWorkspace.TerrainPresentation);
        Assert.Equal([new TerrainRun<RoughnessClass>(0, 1, RoughnessClass.Calm), new(1, 2, RoughnessClass.Rough),
            new(3, 6, RoughnessClass.Rough)],
            presentation.RoughnessRuns);
        Assert.Equal(GroundSensors.Frame, presentation.Coverage.Source);
        Assert.Equal(0.6, presentation.Coverage.SourceShares[(int)GroundSensors.Frame]);
        Assert.Equal(0.4, presentation.Coverage.SourceShares[(int)GroundSensors.Fork]);
        Assert.Equal(forkClipped, presentation.Coverage.ForkClipped);
        // Window 4 has a grade of 0 and draws the grade of its piece, level from its start to its end.
        Assert.Equal(3, presentation.Grade[^1].Seconds[0]);
        Assert.Equal(6, presentation.Grade[^1].Seconds[^1]);
        Assert.All(presentation.Grade[^1].Values, value => Assert.Equal(Math.Asin(0.1) * 180 / Math.PI, value, 8));
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task PedalBobRowDrawsOnlyWindowsWithDetectedPedalling()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        PedallingFigures Pedalled(double bob) => new(true, 80, bob);
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0) with { Pedalling = Pedalled(7) },
            TerrainProfiles.Window(1) with { Pedalling = Pedalled(9) },
            TerrainProfiles.Window(2) with { Pedalling = new(false, null, null) },
            TerrainProfiles.Window(3) with { Pedalling = Pedalled(8) }]);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, profile)));
        await h.LoadAsync(editor);
        Assert.True(editor.SignalsWorkspace.PedalBobSignalState.IsReady);
        var bob = editor.SignalsWorkspace.TerrainPresentation!.PedalBob;
        Assert.Equal([[7d, 9d], [8d]], bob.Select(section => section.Values.ToArray()));
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task PedalBobRowIsHiddenWithoutPedallingFigures()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, RoughProfile())));
        await h.LoadAsync(editor);
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.PedalBobSignalState.IsHidden);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task PendingTerrainIsPrioritizedAndShowsBusyRows()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(SessionDetailHarness.DomainFromSnapshot(session)));
        await h.LoadAsync(editor);
        h.TerrainCoordinator.Received().Prioritize(session.Id);
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.ShowSpinner);
        await h.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(2048d, true)]
    [InlineData(null, false)]
    public async Task PublishedProfileReplacesTheBusyRowsAndShowsRoughnessOnlyWithAnImu(double? frameScale, bool roughness)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = SessionDetailHarness.DomainFromSnapshot(session);
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0)], TerrainGradeState.NoElevation,
            frameScale, null);
        changes.OnNext(h.WithTerrain(session, profile));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        Assert.Equal(roughness, editor.SignalsWorkspace.RoughnessSignalState.IsReady);
        Assert.Equal(!roughness, editor.SignalsWorkspace.RoughnessSignalState.IsHidden);
        Assert.True(editor.SignalsWorkspace.GradeSignalState.IsHidden);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task MapColouringChoiceIsStoredAndColoursTheTrack()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, RoughProfile())));
        await h.LoadAsync(editor);
        var map = editor.MediaWorkspace.MapViewModel!;
        map.TrackColoring = MapTrackColoring.Roughness;
        await ViewTestHelpers.FlushDispatcherAsync();
        var stored = Assert.IsType<SessionPreferenceLoadResult.Loaded>(await h.SessionCoordinator.LoadPreferencesAsync(session.Id));
        Assert.Equal(MapTrackColoring.Roughness, stored.Preferences.MapTrackColoring);
        Assert.NotEmpty(map.TerrainRuns);
        await h.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(MapTrackColoring.Plain, new string[0])]
    [InlineData(MapTrackColoring.Roughness, new[] { "Smooth", "Calm", "Medium", "Rough", "Very rough", "Smooth climb", "Technical climb", "No class" })]
    [InlineData(MapTrackColoring.Grade, new[] { "Steep climb", "Climb", "Flat", "Gentle descent", "Descent", "Steep descent",
        "Very steep descent", "No class" })]
    public async Task MapLegendListsTheClassesOfTheChosenColouring(MapTrackColoring coloring, string[] labels)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, RoughProfile())));
        await h.LoadAsync(editor);
        var map = editor.MediaWorkspace.MapViewModel!;
        map.TrackColoring = coloring;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(labels, map.TerrainLegend.Select(entry => entry.Label));
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task FailedTerrainShowsTheErrorRowAndClearsTheMap()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var map = editor.MediaWorkspace.MapViewModel!;
        map.TrackColoring = MapTrackColoring.Roughness;
        await ViewTestHelpers.FlushDispatcherAsync();
        changes.OnNext(domain with { Terrain = new TerrainStatus.Failed() });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(map.HasTerrain);
        Assert.Empty(map.TerrainRuns);
        Assert.Equal(SurfaceStateKind.Error, editor.SignalsWorkspace.TerrainSignalState.Kind);
        Assert.True(editor.SignalsWorkspace.RoughnessSignalState.IsHidden);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task UnavailableTerrainHidesTheRows()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = SessionDetailHarness.DomainFromSnapshot(session) with
        {
            Terrain = new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack)
        };
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(domain));
        await h.LoadAsync(editor);
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsHidden);
        Assert.True(editor.SignalsWorkspace.RoughnessSignalState.IsHidden);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task LateProfileAfterOffsetChangeLeavesTheNewRowsPending()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var key = SessionTerrainStatusQuery.Evaluate(session, []).Key;
        var profile = TerrainProfiles.Profile([], TerrainGradeState.NoFrameSensor, null, null);
        var domain = SessionDetailHarness.DomainFromSnapshot(session);
        var current = new TerrainStatus.Current(key, TerrainProfileCalculator.Summarize(profile));
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain with { Terrain = current });
        var release = new TaskCompletionSource();
        h.TerrainProfiles.Seed(session.Id, key, profile);
        h.TerrainProfiles.BeforeRead = (_, _) => release.Task;
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        changes.OnNext(domain with { Session = session with { GpsOffsetSeconds = 2 }, Terrain = new TerrainStatus.Pending() });
        await ViewTestHelpers.FlushDispatcherAsync();
        release.SetResult();
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.ShowSpinner);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ScaleChangeReclassesTheRideAndKeepsTheChosenScope()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var scope = editor.SignalsWorkspace.TerrainScope!;
        scope.Roughness[(int)RoughnessClass.Rough].Select.Execute(null);
        changes.OnNext(domain with { TerrainScale = OneStepRougherMedian() });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(RoughnessClass.Medium, Assert.Single(editor.SignalsWorkspace.TerrainPresentation!.RoughnessRuns).Class);
        Assert.True(scope.Roughness[(int)RoughnessClass.Rough].IsChecked);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ClassesOfAnEarlierScaleArrivingLastAreDropped()
    {
        var runner = new DeferredBackgroundTaskRunner();
        var dispatcher = new ManualUiThreadDispatcher();
        using var h = new SessionDetailHarness { BackgroundRunner = runner, UiThreadDispatcher = dispatcher };
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var earlierRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        h.TerrainProfiles.BeforeRead = (_, _) => ++reads == 1 ? earlierRead.Task : Task.CompletedTask;
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await runner.DrainUntilAsync(dispatcher, h.LoadAsync(editor));
        changes.OnNext(domain with { TerrainScale = OneStepRougherMedian() });
        await dispatcher.DrainAsync();
        await runner.RunNextAsync();
        await dispatcher.DrainAsync();
        earlierRead.SetResult();
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DrainAsync();
        Assert.Equal(RoughnessClass.Medium, Assert.Single(editor.SignalsWorkspace.TerrainPresentation!.RoughnessRuns).Class);
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task LabelChangeReclassesTheRideAndKeepsTheChosenScope()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var scope = editor.SignalsWorkspace.TerrainScope!;
        scope.Roughness[(int)RoughnessClass.Rough].Select.Execute(null);
        changes.OnNext(domain with { TerrainLabels = SmoothLabel() });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(RoughnessClass.Smooth, Assert.Single(editor.SignalsWorkspace.TerrainPresentation!.RoughnessRuns).Class);
        Assert.True(scope.Roughness[(int)RoughnessClass.Rough].IsChecked);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ClassesOfAnEarlierLabelSetArrivingLastAreDropped()
    {
        var runner = new DeferredBackgroundTaskRunner();
        var dispatcher = new ManualUiThreadDispatcher();
        using var h = new SessionDetailHarness { BackgroundRunner = runner, UiThreadDispatcher = dispatcher };
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var earlierRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reads = 0;
        h.TerrainProfiles.BeforeRead = (_, _) => ++reads == 1 ? earlierRead.Task : Task.CompletedTask;
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await runner.DrainUntilAsync(dispatcher, h.LoadAsync(editor));
        changes.OnNext(domain with { TerrainLabels = SmoothLabel() });
        await dispatcher.DrainAsync();
        await runner.RunNextAsync();
        await dispatcher.DrainAsync();
        earlierRead.SetResult();
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        await dispatcher.DrainAsync();
        Assert.Equal(RoughnessClass.Smooth, Assert.Single(editor.SignalsWorkspace.TerrainPresentation!.RoughnessRuns).Class);
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task EqualTerrainFromANewSnapshotKeepsTheLoadedTerrain()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var loaded = editor.SignalsWorkspace.TerrainPresentation;
        changes.OnNext(domain with { Session = session with { Description = "Changed" } });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Same(loaded, editor.SignalsWorkspace.TerrainPresentation);
        await h.CloseAsync(editor);
    }

    private static SetupTerrainScale OneStepRougherMedian() =>
        TestSnapshots.TerrainScale(TerrainProfiles.Scale with { FrameImu = 20 });

    private static TerrainLabelSet SmoothLabel() => TerrainProfiles.Labels(new TerrainLabelRange(0, 1, RoughnessClass.Smooth));

    private static TerrainProfile RoughProfile() =>
        TerrainProfiles.Profile([TerrainProfiles.Window(0, RoughnessClass.Rough)]);
}
