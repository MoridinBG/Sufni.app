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
    private readonly BehaviorSubject<SessionTerrainInput?> analyzedTerrain = new(null);
    private readonly List<TerrainBaseline?> analyzedBaselines = [];
    private BehaviorSubject<RecordedSessionDomainSnapshot>? domains;

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
        await runner.DrainUntilAsync(dispatcher, analyzedTerrain.FirstAsync(terrain => terrain?.Baseline is TerrainBaseline.Failed)
            .ToTask(TestContext.Current.CancellationToken));
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task ScaleChangeKeepsTheFittedBaseline()
    {
        using var h = CreateHarness();
        var (editor, _) = await OpenWithBaselineAsync(h, baselineRides: 5, fitted: true);
        domains!.OnNext(domains.Value with { TerrainScale = TestSnapshots.TerrainScale(SyntheticRide.Scale) });
        var rescaled = await RunUntilAnalyzedAsync(terrain => terrain?.Classified.Scale == SyntheticRide.Scale);
        Assert.IsType<TerrainBaseline.Fitted>(rescaled!.Baseline);
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    private async Task<SessionTerrainInput?> RunUntilAnalyzedAsync(Func<SessionTerrainInput?, bool> predicate)
    {
        var analyzed = analyzedTerrain.FirstAsync(predicate).ToTask(TestContext.Current.CancellationToken);
        await runner.DrainUntilAsync(dispatcher, analyzed);
        return await analyzed;
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
            var terrain = call.Arg<SessionInsightsRequest>().Terrain;
            analyzedBaselines.Add(terrain?.Baseline);
            analyzedTerrain.OnNext(terrain);
            return SessionInsightsResult.Hidden;
        });
        h.SessionSummaries.AddOrUpdate(rows[..baselineRides]);
        domains = new BehaviorSubject<RecordedSessionDomainSnapshot>(domain);
        var editor = h.CreateRecordedEditor(session, projectionChanges: domains);
        await runner.DrainUntilAsync(dispatcher, h.LoadAsync(editor));
        editor.AnalysisWorkspace.RequestSessionInsights();
        await runner.DrainUntilAsync(dispatcher, AnalyzedAsync(fitted, baselineRides));
        return (editor, rows);
    }

    private Task AnalyzedAsync(bool fitted, int sessions) => analyzedTerrain.FirstAsync(terrain => terrain?.Baseline switch
    {
        TerrainBaseline.Fitted fit => fitted && fit.Model.Sessions == sessions,
        TerrainBaseline.Insufficient limited => !fitted && limited.Model.Sessions == sessions,
        _ => false
    }).ToTask(TestContext.Current.CancellationToken);
}
