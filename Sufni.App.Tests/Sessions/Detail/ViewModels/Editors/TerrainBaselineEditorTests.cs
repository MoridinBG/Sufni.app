using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using Avalonia.Headless.XUnit;
using DynamicData;
using NSubstitute;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Terrain.Models;
using Sufni.App.Tests.TestSupport.Fixtures;
using Sufni.App.Tests.TestSupport.Sessions;
using Sufni.Telemetry.Tests;

namespace Sufni.App.Tests.Sessions.Detail.ViewModels.Editors;

[Collection("Ui")]
public class TerrainBaselineEditorTests
{
    private readonly DeferredBackgroundTaskRunner runner = new();
    private readonly ManualUiThreadDispatcher dispatcher = new();
    private readonly BehaviorSubject<TerrainBaseline?> analyzedBaseline = new(null);
    private readonly List<TerrainBaseline?> analyzedBaselines = [];

    [AvaloniaFact]
    public async Task FifthRideCommittedWhileOpenGivesInsightsAFittedBaseline()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 4, fitted: false);
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await runner.DrainUntilAsync(dispatcher, AnalyzedAsync(fitted: true, 5));
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task RideWhoseProfileBecomesPendingLeavesTheBaseline()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 5, fitted: true);
        h.SessionSummaries.AddOrUpdate(rows[4] with { Terrain = new TerrainStatus.Pending() });
        await runner.DrainUntilAsync(dispatcher, AnalyzedAsync(fitted: false, 4));
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task FitCompletedBeforeTheBaselineChangedDoesNotPublish()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 4, fitted: false);
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await dispatcher.DrainAsync();
        await runner.RunNextAsync();
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        h.SessionSummaries.AddOrUpdate(rows[5]);
        await dispatcher.DrainAsync();
        await runner.DrainUntilAsync(dispatcher, AnalyzedAsync(fitted: true, 6));
        Assert.DoesNotContain(analyzedBaselines, baseline => baseline is TerrainBaseline.Fitted { Model.Sessions: 5 });
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task BaselineReadFailureGivesInsightsAFailedBaselineAndKeepsTheRideTerrain()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 4, fitted: false);
        h.TerrainProfiles.ReadFailure = new IOException("Baseline unavailable.");
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await runner.DrainUntilAsync(dispatcher, analyzedBaseline.FirstAsync(baseline => baseline is TerrainBaseline.Failed)
            .ToTask(TestContext.Current.CancellationToken));
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    private SessionDetailHarness CreateHarness() => new() { BackgroundRunner = runner, UiThreadDispatcher = dispatcher };

    private async Task<(SessionDetailViewModel Editor, RecordedSessionSummary[] Rows)> OpenWithBaselineAsync(
        SessionDetailHarness h, int baselineRides, bool fitted)
    {
        var profiles = SyntheticResponseRides.Baseline;
        var session = h.CreateRecordedSession();
        var bike = TestSnapshots.Bike();
        var domain = h.WithTerrain(session, profiles[0]) with { Setup = TestSnapshots.Setup(bikeId: bike.Id), Bike = bike };
        var rows = profiles.Select((_, i) => new RecordedSessionSummary(Guid.NewGuid(), i, "baseline", "", null, true,
            new SessionStaleness.Current(), BikeId: bike.Id) { Terrain = domain.Terrain }).ToArray();
        var key = ((TerrainStatus.Current)domain.Terrain).Key;
        foreach (var (index, row) in rows.Index()) h.TerrainProfiles.Seed(row.Id, key, profiles[index]);
        h.SessionInsightsService.Analyze(Arg.Any<SessionInsightsRequest>()).Returns(call =>
        {
            var baseline = call.Arg<SessionInsightsRequest>().Terrain?.Baseline;
            analyzedBaselines.Add(baseline);
            analyzedBaseline.OnNext(baseline);
            return SessionInsightsResult.Hidden;
        });
        h.SessionSummaries.AddOrUpdate(rows[..baselineRides]);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(domain));
        await runner.DrainUntilAsync(dispatcher, h.LoadAsync(editor));
        editor.AnalysisWorkspace.RequestSessionInsights();
        await runner.DrainUntilAsync(dispatcher, AnalyzedAsync(fitted, baselineRides));
        return (editor, rows);
    }

    private Task AnalyzedAsync(bool fitted, int sessions) => analyzedBaseline.FirstAsync(baseline => baseline switch
    {
        TerrainBaseline.Fitted fit => fitted && fit.Model.Sessions == sessions,
        TerrainBaseline.Insufficient limited => !fitted && limited.Model.Sessions == sessions,
        _ => false
    }).ToTask(TestContext.Current.CancellationToken);
}
