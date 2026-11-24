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
    public async Task RoughChipNarrowsTheDampingShareToTheRoughSectionAndClearingRestoresTheWholeRide()
    {
        using var h = new SessionDetailHarness();
        var ride = SyntheticRide.Build([new(140, LowBandG: 0.2, MiddleBandG: 0.2, HighBandG: 0.2),
            new(140, LowBandG: 1.5, MiddleBandG: 1.6, HighBandG: 1.8)]);
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, ride.Profile());
        var editor = h.CreateRecordedEditor(session, h.CreateLoadedResult(ride.Telemetry), Observable.Return(domain));
        await h.LoadAsync(editor);
        var filter = editor.SignalsWorkspace.TerrainFilter!;
        var rough = filter.Roughness[(int)RoughnessClass.Rough];
        Assert.InRange(rough.DistanceMetres, 120, 150);
        var analysis = editor.AnalysisWorkspace;
        var full = analysis.DampingPercentages;
        rough.Toggle.Execute(null);
        Assert.Equal(rough.DistanceMetres, filter.SelectedMetres);
        Assert.True(analysis.DampingPercentages.FrontHscPercentage > full.FrontHscPercentage);
        filter.Clear.Execute(null);
        Assert.Equal(full, analysis.DampingPercentages);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task ClassFilterAndComparisonReplaceEachOtherAndNewTelemetryClearsTheFilter()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        using var changes = new Subject<RecordedSessionDomainSnapshot>();
        var compared = SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), TestSegmentMatches.Telemetry(), 30)!;
        TestSegmentComparisons.DetailReturns(h.SegmentCoordinator, compared);
        var editor = await h.LoadMatchedEditorAsync(session, changes, compared);
        var signals = editor.SignalsWorkspace;
        var filter = signals.TerrainFilter!;
        var chip = filter.Roughness[(int)RoughnessClass.Rough];
        chip.Toggle.Execute(null);
        Assert.True(filter.IsActive);
        var comparisons = signals.SegmentComparison;
        var row = Assert.Single(comparisons.TravelRows);
        await ((IAsyncRelayCommand)row.HeaderActions.Single(a => a.Command is IAsyncRelayCommand).Command!).ExecuteAsync(null);
        Assert.True(comparisons.HasAnalysisComparison);
        Assert.False(filter.IsActive);
        chip.Toggle.Execute(null);
        Assert.True(filter.IsActive);
        comparisons.ClearAnalysisComparisonCommand.Execute(null);
        Assert.True(filter.IsActive);
        var replacement = session with { ProcessedTelemetryRevision = session.ProcessedTelemetryRevision + 1 };
        h.ConfigureLoad(replacement, h.CreateLoadedResult());
        changes.OnNext(SessionDetailHarness.DomainFromSnapshot(replacement));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(filter.IsActive);
        Assert.False(chip.IsChecked);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task GpsOffsetEditEndsTheActiveClassFilter()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, RoughProfile());
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var filter = editor.SignalsWorkspace.TerrainFilter!;
        filter.Roughness[(int)RoughnessClass.Rough].Toggle.Execute(null);
        Assert.True(filter.IsActive);
        changes.OnNext(domain with { Session = session with { GpsOffsetSeconds = 2 }, Terrain = new TerrainStatus.Pending() });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(filter.IsActive);
        await h.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StripBreaksAtStopsAndReportsEachSensorContribution(bool jointlyRecorded)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var first = jointlyRecorded ? RoughnessSource.Both : RoughnessSource.LowersOnly;
        var window = TerrainProfiles.Window(0, RoughnessClass.Calm, first);
        var clipped = TerrainProfiles.Window(3, RoughnessClass.Rough, RoughnessSource.FrameOnly) with
        {
            Lowers = jointlyRecorded ? window.Lowers! with { ClippedShare = 0.12 } : null
        };
        var profile = TerrainProfiles.Classified(window, TerrainProfiles.Window(1, RoughnessClass.Rough, first),
            TerrainProfiles.Window(2, RoughnessClass.Rough, first) with { Moving = false, Grade = null }, clipped,
            TerrainProfiles.Window(4, RoughnessClass.Rough, RoughnessSource.FrameOnly),
            TerrainProfiles.Window(5, RoughnessClass.Rough, RoughnessSource.FrameOnly, 0.3));
        var domain = h.WithTerrain(session, profile);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(domain));
        await h.LoadAsync(editor);
        var presentation = Assert.IsType<TerrainPresentation>(editor.SignalsWorkspace.TerrainPresentation);
        Assert.Equal([new TerrainClassRun(0, 1, TerrainLevel.Low), new TerrainClassRun(1, 2, TerrainLevel.High),
            new TerrainClassRun(3, 6, TerrainLevel.High)],
            presentation.RoughnessRuns);
        Assert.Equal(RoughnessSource.Both, presentation.Coverage.Source);
        Assert.Equal(0.6, presentation.Coverage.FrameOnlyShare);
        Assert.Equal(jointlyRecorded ? 0.4 : 0, presentation.Coverage.JointShare);
        Assert.Equal(jointlyRecorded, presentation.Coverage.LowersClipped);
        Assert.Equal(4.5, presentation.Grade[^1].Seconds[^2]);
        Assert.Equal(Math.Asin(0.1) * 180 / Math.PI, presentation.Grade[^1].Values[^2], 8);
        await h.CloseAsync(editor);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceNoteReportsAnIgnoredSensorEvenWithoutClassifiedWindows(bool silent)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0), TerrainProfiles.Window(1)],
            TerrainRoughnessState.NoSensor, frameScale: null, lowersScale: null) with
        {
            SilentSensors = silent ? [TerrainSensor.Frame] : []
        };
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(h.WithTerrain(session, profile)));
        await h.LoadAsync(editor);
        Assert.Equal(silent, editor.SignalsWorkspace.TerrainSourceDescription is not null);
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

    [AvaloniaFact]
    public async Task PublishedProfileReplacesTheBusyRows()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = SessionDetailHarness.DomainFromSnapshot(session);
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        var profile = TerrainProfiles.Profile([TerrainProfiles.Window(0)], TerrainRoughnessState.ValuesOnly,
            TerrainGradeState.NoElevation, 2048, null);
        changes.OnNext(h.WithTerrain(session, profile));
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.RoughnessSignalState.IsReady);
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
        var profile = TerrainProfiles.Profile([], TerrainRoughnessState.NoSensor, TerrainGradeState.NoFrameSensor, null, null);
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

    private static TerrainProfile RoughProfile() =>
        TerrainProfiles.Classified(TerrainProfiles.Window(0, RoughnessClass.Rough, RoughnessSource.Both));
}
