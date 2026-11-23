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
