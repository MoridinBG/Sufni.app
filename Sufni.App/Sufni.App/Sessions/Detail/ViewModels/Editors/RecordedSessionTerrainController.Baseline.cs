using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using DynamicData;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Common;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Queries;
using Sufni.App.Terrain.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal sealed partial class RecordedSessionTerrainController
{
    private readonly IDisposable baselineSubscription;
    private readonly CancellableOperation planning = new();
    private readonly CancellableOperation fitting = new();
    private Task baselineTasks = Task.CompletedTask;
    private Task latestPlan = Task.CompletedTask;
    private (TerrainProfileKey, TerrainBaselineIdentity)? requestedBaseline;

    private IDisposable ObserveBaseline(Guid id, IObservable<RecordedSessionEditorState> states,
        IRecordedSessionProjection projection, ITerrainProfileRepository repository, IBackgroundTaskRunner worker,
        IUiThreadDispatcher ui, RecordedSessionEditorActions actions, CancellationToken lifetime)
    {
        var source = states.Select(s => (Bike: s.Domain?.Setup?.BikeId,
            Key: (s.Terrain as RecordedTerrainState.Loaded)?.Key, Profile: (s.Terrain as RecordedTerrainState.Loaded)?.Profile))
            .DistinctUntilChanged();
        var summaries = projection.ConnectSessions().ToCollection().StartWith(Array.Empty<RecordedSessionSummary>());
        return source.CombineLatest(summaries, (ride, sessions) => (ride, sessions)).Subscribe(input =>
        {
            var token = planning.Start(lifetime);
            if (input.ride.Key is not { } key || input.ride.Profile is not { } profile)
            {
                fitting.Cancel();
                requestedBaseline = null;
                return;
            }
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            latestPlan = ready.Task;
            Track(PlanAsync(input.sessions, input.ride.Bike, key, profile, token, ready));
        });

        async Task PlanAsync(IReadOnlyCollection<RecordedSessionSummary> sessions, Guid? bike, TerrainProfileKey key,
            TerrainProfile profile, CancellationToken token, TaskCompletionSource ready)
        {
            try
            {
                var identity = await worker.RunAsync(() => TerrainBaselineQuery.Select(id, bike, sessions), token).ConfigureAwait(false);
                await ui.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested || requestedBaseline == (key, identity)) return;
                    requestedBaseline = (key, identity);
                    var fitToken = fitting.Start(lifetime);
                    actions.SetBaseline(key, new TerrainBaseline.NotLoaded(identity));
                    Track(FitAsync(key, profile, identity, fitToken));
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                Log.ForContext<RecordedSessionTerrainController>().Warning(error, "Could not select terrain baseline for {SessionId}", id);
                await ui.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    fitting.Cancel();
                    requestedBaseline = null;
                    actions.SetBaseline(key, new TerrainBaseline.Failed(TerrainBaselineIdentity.Empty));
                });
            }
            finally { ready.TrySetResult(); }
        }

        async Task FitAsync(TerrainProfileKey key, TerrainProfile profile, TerrainBaselineIdentity identity, CancellationToken token)
        {
            TerrainBaseline result;
            try
            {
                var model = await worker.RunAsync(async () =>
                {
                    var expected = identity.Profiles.ToDictionary(p => p.SessionId, p => p.Key);
                    var profiles = expected.Count == 0 ? [] : (await repository.ReadAsync(expected, token).ConfigureAwait(false)).Values.ToArray();
                    return SuspensionResponseModel.Fit(profile, profiles, token);
                }, token).ConfigureAwait(false);
                result = model.HasFit ? new TerrainBaseline.Fitted(identity, model) : new TerrainBaseline.Insufficient(identity, model);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                Log.ForContext<RecordedSessionTerrainController>().Warning(error, "Could not fit terrain baseline for {SessionId}", id);
                result = new TerrainBaseline.Failed(identity);
            }

            while (!token.IsCancellationRequested)
            {
                Task? validating = null;
                await ui.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested) return;
                    if (!latestPlan.IsCompleted) { validating = latestPlan; return; }
                    actions.SetBaseline(key, result);
                });
                if (validating is null) return;
                await validating.ConfigureAwait(false);
            }
        }

        void Track(Task task) => baselineTasks = baselineTasks.IsCompleted ? task : Task.WhenAll(baselineTasks, task);
    }
}
