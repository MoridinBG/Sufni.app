using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using Avalonia.Headless.XUnit;
using DynamicData;
using NSubstitute;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Insights.Services.SessionInsights;
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

    [AvaloniaFact]
    public async Task FifthRideCommittedWhileOpenReportsTheResponse()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 4, SessionResponseState.InsufficientBaseline);
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await runner.DrainUntilAsync(dispatcher, ResponseAsync(editor, SessionResponseState.Reported, 5));
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task RideWhoseProfileBecomesPendingLeavesTheBaseline()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 5, SessionResponseState.Reported);
        h.SessionSummaries.AddOrUpdate(rows[4] with { Terrain = new TerrainStatus.Pending() });
        await runner.DrainUntilAsync(dispatcher, ResponseAsync(editor, SessionResponseState.InsufficientBaseline, 4));
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task FitCompletedBeforeTheBaselineChangedDoesNotPublish()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 4, SessionResponseState.InsufficientBaseline);
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await dispatcher.DrainAsync();
        await runner.RunNextAsync();
        await dispatcher.WaitForWorkAsync(TestContext.Current.CancellationToken).AwaitBoundedAsync(TimeSpan.FromSeconds(5));
        h.SessionSummaries.AddOrUpdate(rows[5]);
        await dispatcher.DrainAsync();
        Assert.NotEqual((SessionResponseState.Reported, 5), Response(editor));
        await runner.DrainUntilAsync(dispatcher, ResponseAsync(editor, SessionResponseState.Reported, 6));
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    [AvaloniaFact]
    public async Task BaselineReadFailureShowsTheBaselineErrorAndKeepsTheRideTerrain()
    {
        using var h = CreateHarness();
        var (editor, rows) = await OpenWithBaselineAsync(h, baselineRides: 4, SessionResponseState.InsufficientBaseline);
        h.TerrainProfiles.ReadFailure = new IOException("Baseline unavailable.");
        h.SessionSummaries.AddOrUpdate(rows[4]);
        await runner.DrainUntilAsync(dispatcher, ResponseAsync(editor, SessionResponseState.BaselineError, 0));
        Assert.True(editor.SignalsWorkspace.TerrainSignalState.IsReady);
        await runner.DrainUntilAsync(dispatcher, h.CloseAsync(editor));
    }

    private SessionDetailHarness CreateHarness() => new() { BackgroundRunner = runner, UiThreadDispatcher = dispatcher };

    private async Task<(SessionDetailViewModel Editor, RecordedSessionSummary[] Rows)> OpenWithBaselineAsync(
        SessionDetailHarness h, int baselineRides, SessionResponseState opened)
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
            new SessionInsightsService().Analyze(call.Arg<SessionInsightsRequest>()));
        h.SessionSummaries.AddOrUpdate(rows[..baselineRides]);
        var editor = h.CreateRecordedEditor(session, projectionChanges: Observable.Return(domain));
        await runner.DrainUntilAsync(dispatcher, h.LoadAsync(editor));
        editor.AnalysisWorkspace.RequestSessionInsights();
        await runner.DrainUntilAsync(dispatcher, ResponseAsync(editor, opened, baselineRides));
        return (editor, rows);
    }

    private static (SessionResponseState State, int Sessions) Response(SessionDetailViewModel editor)
    {
        var response = editor.AnalysisWorkspace.SessionInsights.Response;
        return (response.State, response.BaselineSessions);
    }

    private static Task ResponseAsync(SessionDetailViewModel editor, SessionResponseState state, int sessions)
    {
        var workspace = (INotifyPropertyChanged)editor.AnalysisWorkspace;
        return Observable.FromEventPattern<PropertyChangedEventHandler, PropertyChangedEventArgs>(
                handler => workspace.PropertyChanged += handler, handler => workspace.PropertyChanged -= handler)
            .Select(_ => Response(editor)).StartWith(Response(editor))
            .FirstAsync(response => response == (state, sessions)).ToTask(TestContext.Current.CancellationToken);
    }
}
