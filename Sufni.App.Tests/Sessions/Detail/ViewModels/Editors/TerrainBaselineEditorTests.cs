using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Headless.XUnit;
using DynamicData;
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Insights.Services.SessionInsights;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Presentation;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

[Collection("Ui")]
public class TerrainBaselineEditorTests
{
    [AvaloniaFact]
    public async Task BaselineTracksPublicationAndRejectsASupersededFitWithoutTabActivation()
    {
        using var h = new SessionDetailHarness { UiThreadDispatcher = new AvaloniaUiThreadDispatcher() };
        var profiles = SyntheticResponseRides.Baseline;
        var session = h.CreateRecordedSession();
        var bike = TestSnapshots.Bike();
        var domain = h.WithTerrain(session, profiles[0]) with { Setup = TestSnapshots.Setup(bikeId: bike.Id), Bike = bike };
        var rows = profiles.Select((_, i) => new RecordedSessionSummary(Guid.NewGuid(), i, "baseline", "", null, true,
            new SessionStaleness.Current(), BikeId: bike.Id) { Terrain = domain.Terrain }).ToArray();
        var byId = rows.Index().ToDictionary(p => p.Item.Id, p => profiles[p.Index]);
        var firstFive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attemptedFive = 0;
        var fail = false;
        h.TerrainProfiles.ReadAsync(Arg.Is<IReadOnlyDictionary<Guid, TerrainProfileKey>>(keys =>
            keys.Count > 0 && !keys.ContainsKey(session.Id)), Arg.Any<CancellationToken>()).Returns(call => ReadAsync(call.Arg<IReadOnlyDictionary<Guid, TerrainProfileKey>>()));
        async Task<IReadOnlyDictionary<Guid, TerrainProfile>> ReadAsync(IReadOnlyDictionary<Guid, TerrainProfileKey> keys)
        {
            if (keys.Count == 5 && Interlocked.Increment(ref attemptedFive) == 1)
            {
                firstFive.SetResult();
                await releaseFive.Task;
            }
            if (fail) throw new IOException("Baseline unavailable.");
            return keys.Keys.ToDictionary(id => id, id => byId[id]);
        }
        h.SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(call =>
            new SessionInsightsService().Analyze(call.Arg<SessionInsightsRequest>()));
        h.SessionSummaries.AddOrUpdate(rows[..4]);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(domain));
        await h.LoadAsync(editor);
        var workspace = editor.AnalysisWorkspace;
        workspace.RequestSessionInsights();
        await WaitForAsync(workspace, p => p.State == SessionResponseState.InsufficientBaseline && p.BaselineSessions == 4);
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await firstFive.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        h.SessionSummaries.AddOrUpdate(rows[5]);
        await WaitForAsync(workspace, p => p.State == SessionResponseState.Reported && p.BaselineSessions == 6);
        releaseFive.SetResult();
        h.SessionSummaries.AddOrUpdate(rows[5] with { Terrain = new TerrainStatus.Pending() });
        var report = await WaitForAsync(workspace, p => p.State == SessionResponseState.Reported && p.BaselineSessions == 5);
        Assert.Equal(4, report.Rows.Length);
        h.SessionSummaries.AddOrUpdate(rows[4] with { Terrain = new TerrainStatus.Pending() });
        await WaitForAsync(workspace, p => p.State == SessionResponseState.InsufficientBaseline && p.BaselineSessions == 4);
        fail = true;
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await WaitForAsync(workspace, p => p.State == SessionResponseState.BaselineError);
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        Assert.True(editor.MediaWorkspace.MapViewModel!.HasTerrain);
        await h.CloseAsync(editor);
    }

    private static Task<SessionResponsePanel> WaitForAsync(ISessionAnalysisWorkspace workspace, Func<SessionResponsePanel, bool> condition)
    {
        var source = (INotifyPropertyChanged)workspace;
        return Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => source.PropertyChanged += handler, handler => source.PropertyChanged -= handler)
            .Select(_ => workspace.SessionInsights.Response).StartWith(workspace.SessionInsights.Response)
            .FirstAsync(condition).ToTask(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
    }
}
