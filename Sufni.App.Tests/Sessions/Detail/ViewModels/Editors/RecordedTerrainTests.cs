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
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StripBreaksAtStopsAndReportsEachSensorContribution(bool jointlyRecorded)
    {
        using var h = new SessionDetailHarness();
        var session = h.CreateRecordedSession();
        var key = SessionTerrainStatusQuery.Evaluate(session, []).Key;
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
        var domain = SessionDetailHarness.DomainFromSnapshot(session) with
        {
            Terrain = new TerrainStatus.Current(key, TerrainProfileCalculator.Summarize(profile))
        };
        h.TerrainProfiles.ReadAsync(Arg.Any<IReadOnlyDictionary<Guid, TerrainProfileKey>>(), Arg.Any<CancellationToken>())
            .Returns(new Dictionary<Guid, TerrainProfile> { [session.Id] = profile });
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
        Assert.Equal(4.5, presentation.Signals[^2].MidSeconds);
        Assert.Equal(Math.Asin(0.1) * 180 / Math.PI, presentation.Signals[^2].GradeDegrees!.Value, 8);
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

        changes.OnNext(domain with { Terrain = new TerrainStatus.Failed() });
        await ViewTestHelpers.FlushDispatcherAsync();
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
