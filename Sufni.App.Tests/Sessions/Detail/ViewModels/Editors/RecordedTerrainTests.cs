using Sufni.App.Segments.Models;
using Sufni.App.Segments.Services;
using Sufni.App.Segments.Coordinators;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Sessions.Analysis.Services;
using Sufni.Telemetry.Tests;
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
    public async Task RoughChipChangesTravelStatisticsAndClearingRestoresTheWholeRide()
    {
        using var h = new SessionDetailHarness();
        var ride = SyntheticRide.Build([new(140, LowBandG: 0.2, MiddleBandG: 0.2, HighBandG: 0.2),
            new(140, LowBandG: 1.5, MiddleBandG: 1.6, HighBandG: 1.8)]);
        var profile = Assert.IsType<TerrainProfileResult.Computed>(TerrainProfileCalculator.Calculate(ride.Telemetry, ride.Fixes)).Profile;
        var session = h.CreateRecordedSession();
        var domain = h.WithTerrain(session, profile);
        var editor = h.CreateRecordedEditor(session, h.CreateLoadedResult(ride.Telemetry), Observable.Return(domain));
        await h.LoadAsync(editor);
        var filter = editor.SignalsWorkspace.TerrainFilter!;
        var rough = filter.Roughness[(int)RoughnessClass.Rough];
        Assert.InRange(rough.DistanceMetres, 120, 150);
        var full = await TravelAsync();
        rough.Toggle.Execute(null);
        Assert.True(filter.IsActive);
        Assert.True(rough.IsChecked);
        Assert.Equal(rough.DistanceMetres, filter.SelectedMetres);
        var filtered = await TravelAsync();
        Assert.True(filtered.Average > full.Average);
        filter.Clear.Execute(null);
        Assert.False(filter.IsActive);
        Assert.Equal(full, await TravelAsync());
        await h.CloseAsync(editor);

        async Task<TravelStatistics> TravelAsync()
        {
            var results = editor.SignalsWorkspace.AnalysisResultState;
            var current = results.CurrentInputs!.CreateKey(RecordedSessionAnalysisFamily.TravelDistribution, SuspensionType.Front);
            await results.RequestAsync(current, TestContext.Current.CancellationToken);
            return Assert.IsType<TravelDistributionAnalysisResult>(results.Get(current)).Statistics;
        }
    }

    [AvaloniaFact]
    public async Task ClassFilterAndComparisonReplaceEachOtherAndNewTelemetryClearsTheFilter()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var telemetry = TestSegmentMatches.Telemetry();
        var profile = TerrainProfiles.Classified(TerrainProfiles.Window(2, RoughnessClass.Rough, RoughnessSource.Both));
        var domain = h.WithTerrain(session, profile);
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, h.CreateLoadedResult(telemetry,
            trackPoints: [new(1000, 0, 0, null), new(1030, 200, 50, null)]), changes);
        await h.LoadAsync(editor);
        var signals = editor.SignalsWorkspace;
        signals.SetAnalysisRange(2, 5);
        var filter = signals.TerrainFilter!;
        var chip = filter.Roughness[(int)RoughnessClass.Rough];
        chip.Toggle.Execute(null);
        Assert.True(filter.IsActive);
        var compared = SegmentComparisonBuilder.Build(TestSegmentMatches.Match(10, 13), telemetry, 30)!;
        h.SegmentCoordinator.MatchAsync(Arg.Any<SegmentMatchSource>(), Arg.Any<CancellationToken>())
            .Returns(new SegmentMatchResult.Matched([compared.Match], new SegmentComparisonPage([compared], [])));
        h.SegmentCoordinator.LoadComparisonDetailAsync(compared, Arg.Any<CancellationToken>())
            .Returns(new SegmentComparisonDetailResult.Loaded(SegmentComparisonBuilder.BuildDetail(telemetry, [])));
        var comparisons = signals.SegmentComparison;
        await comparisons.MatchCommand.ExecuteAsync(null);
        var row = Assert.Single(comparisons.TravelRows);
        await ((IAsyncRelayCommand)row.HeaderActions.Single(a => a.Command is IAsyncRelayCommand).Command!).ExecuteAsync(null);
        Assert.True(comparisons.HasAnalysisComparison);
        Assert.False(filter.IsActive);
        chip.Toggle.Execute(null);
        Assert.False(comparisons.HasAnalysisComparison);
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

    [AvaloniaFact]
    public async Task OpenEditorPrioritizesPendingAndShowsTheProfileWhenPublished()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var domain = SessionDetailHarness.DomainFromSnapshot(session);
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        h.TerrainCoordinator.Received().Prioritize(session.Id);
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.ShowSpinner);

        var key = SessionTerrainStatusQuery.Evaluate(session, []).Key;
        var profile = new TerrainProfile([new TerrainWindow(0, 0, 2, 5, true)], [], [],
            TerrainRoughnessState.ValuesOnly, TerrainSteepnessState.NoElevation, 2048, null);
        h.TerrainProfiles.ReadAsync(Arg.Is<IReadOnlyDictionary<Guid, TerrainProfileKey>>(
            keys => keys[session.Id] == key), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TerrainProfile> { [session.Id] = profile });
        changes.OnNext(domain with { Terrain = new TerrainStatus.Current(key, TerrainProfileCalculator.Summarize(profile)) });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.RoughnessSignalState.IsReady);
        Assert.True(editor.SignalsWorkspace.GradeSignalState.IsHidden);

        var map = editor.MediaWorkspace.MapViewModel!;
        Assert.True(map.HasTerrain);
        map.TrackColoring = MapTrackColoring.Roughness;
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.Equal(MapTrackColoring.Roughness, map.TrackColoring);

        changes.OnNext(domain with { Terrain = new TerrainStatus.Failed() });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.False(map.HasTerrain);
        Assert.Empty(map.TerrainRuns);
        Assert.Equal(SurfaceStateKind.Error, editor.SignalsWorkspace.TerrainSignalState.Kind);
        Assert.True(editor.SignalsWorkspace.RoughnessSignalState.IsHidden);
        changes.OnNext(domain with { Terrain = new TerrainStatus.Unavailable(TerrainAvailabilityReason.NoTrack) });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsHidden);
        await h.CloseAsync(editor);
    }

    [AvaloniaFact]
    public async Task LateProfileAfterOffsetChangeLeavesTheNewRowsPending()
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var key = SessionTerrainStatusQuery.Evaluate(session, []).Key;
        var profile = new TerrainProfile([], [], [], TerrainRoughnessState.NoSensor, TerrainSteepnessState.NoFrameSensor, null, null);
        var domain = SessionDetailHarness.DomainFromSnapshot(session);
        var current = new TerrainStatus.Current(key, TerrainProfileCalculator.Summarize(profile));
        using var changes = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain with { Terrain = current });
        var result = new TaskCompletionSource<IReadOnlyDictionary<Guid, TerrainProfile>>();
        h.TerrainProfiles.ReadAsync(Arg.Any<IReadOnlyDictionary<Guid, TerrainProfileKey>>(), Arg.Any<CancellationToken>())
            .Returns(result.Task);
        var editor = h.CreateRecordedEditor(session, projectionChanges: changes);
        await h.LoadAsync(editor);
        changes.OnNext(domain with { Session = session with { GpsOffsetSeconds = 2 }, Terrain = new TerrainStatus.Pending() });
        await ViewTestHelpers.FlushDispatcherAsync();
        result.SetResult(new Dictionary<Guid, TerrainProfile> { [session.Id] = profile });
        await ViewTestHelpers.FlushDispatcherAsync();
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.ShowSpinner);
        await h.CloseAsync(editor);
    }
}
