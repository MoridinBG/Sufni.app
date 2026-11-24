using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Shared.Common;
using Sufni.App.Terrain.Coordinators;
using Sufni.App.Terrain.Models;
using Sufni.App.Terrain.Services;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Detail.ViewModels.Editors;

internal closed record RecordedTerrainState
{
    public sealed record Pending : RecordedTerrainState;
    public sealed record Loaded(TerrainProfileKey Key, TerrainProfile Profile, TerrainPresentation Presentation) : RecordedTerrainState
    {
        public TerrainBaseline Baseline { get; init; } = new TerrainBaseline.NotLoaded(TerrainBaselineIdentity.Empty);
    }
    public sealed record Unavailable(TerrainAvailabilityReason Reason) : RecordedTerrainState;
    public sealed record Failed : RecordedTerrainState;

    public static RecordedTerrainState FromStatus(TerrainStatus status) => status switch
    {
        TerrainStatus.Unavailable unavailable => new Unavailable(unavailable.Reason),
        TerrainStatus.Failed => new Failed(),
        _ => new Pending()
    };
}

internal sealed partial class RecordedSessionTerrainController : IAsyncDisposable
{
    private readonly IDisposable subscription;
    private readonly CancellableOperation read = new();
    private Task pending = Task.CompletedTask;

    public RecordedSessionTerrainController(Guid id, IObservable<RecordedSessionEditorState> states,
        ITerrainProfileRepository repository, ITerrainProfileCoordinator coordinator, IBackgroundTaskRunner background,
        IUiThreadDispatcher dispatcher, RecordedSessionEditorActions actions, IRecordedSessionProjection projection, CancellationToken lifetime)
    {
        subscription = states.Select(state => state.Domain?.Terrain).DistinctUntilChanged().Subscribe(status =>
        {
            read.Cancel();
            if (lifetime.IsCancellationRequested) return;
            if (status is TerrainStatus.Pending) coordinator.Prioritize(id);
            if (status is not TerrainStatus.Current current) return;
            var next = ReadAsync(current, read.Start(lifetime));
            pending = pending.IsCompleted ? next : Task.WhenAll(pending, next);
        });

        baselineSubscription = ObserveBaseline(id, states, projection, repository, background, dispatcher, actions, lifetime);

        async Task ReadAsync(TerrainStatus.Current current, CancellationToken token)
        {
            RecordedTerrainState result;
            try
            {
                result = await background.RunAsync<RecordedTerrainState>(async () =>
                {
                    var profiles = await repository.ReadAsync(new Dictionary<Guid, TerrainProfileKey> { [id] = current.Key }, token)
                        .ConfigureAwait(false);
                    return profiles.TryGetValue(id, out var profile)
                        ? new RecordedTerrainState.Loaded(current.Key, profile, RecordedSessionPresentationDeriver.CreateTerrain(profile)) : new RecordedTerrainState.Pending();
                }, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
            catch (Exception error)
            {
                Log.ForContext<RecordedSessionTerrainController>().Warning(error, "Could not load terrain for {SessionId}", id);
                result = new RecordedTerrainState.Failed();
            }
            await dispatcher.InvokeAsync(() =>
            {
                if (!token.IsCancellationRequested) actions.SetTerrain(current, result);
            });
        }
    }

    public async ValueTask DisposeAsync()
    {
        subscription.Dispose();
        baselineSubscription.Dispose();
        fitting.Dispose();
        read.Dispose();
        await Task.WhenAll(pending, baselineTasks).ConfigureAwait(false);
    }
}
