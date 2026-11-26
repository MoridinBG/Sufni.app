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
    public sealed record Loaded(TerrainProfileKey Key, ClassifiedProfile Classified, TerrainPresentation Presentation) : RecordedTerrainState
    {
        public TerrainBaseline Baseline { get; init; } = new TerrainBaseline.NotLoaded(TerrainBaselineIdentity.Empty);
    }
    public sealed record Unavailable(TerrainAvailabilityReason Reason) : RecordedTerrainState;
    public sealed record Failed : RecordedTerrainState;

    /// <summary>The scale that the loaded classes follow; a session without a setup scale has no ready bin.</summary>
    public static TerrainScaleKey ScaleOf(RecordedSessionDomainSnapshot? domain) => domain?.TerrainScale?.Key ?? TerrainScaleKey.None;

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
        subscription = states.Select(state => (Status: state.Domain?.Terrain, Scale: RecordedTerrainState.ScaleOf(state.Domain)))
            .DistinctUntilChanged().Subscribe(input =>
        {
            read.Cancel();
            if (lifetime.IsCancellationRequested) return;
            if (input.Status is TerrainStatus.Pending) coordinator.Prioritize(id);
            if (input.Status is not TerrainStatus.Current current) return;
            var next = ReadAsync(current, input.Scale, read.Start(lifetime));
            pending = pending.IsCompleted ? next : Task.WhenAll(pending, next);
        });

        baselineSubscription = ObserveBaseline(id, states, projection, repository, background, dispatcher, actions, lifetime);

        async Task ReadAsync(TerrainStatus.Current current, TerrainScaleKey scale, CancellationToken token)
        {
            RecordedTerrainState result;
            try
            {
                result = await background.RunAsync<RecordedTerrainState>(async () =>
                {
                    var profiles = await repository.ReadAsync(new Dictionary<Guid, TerrainProfileKey> { [id] = current.Key }, token)
                        .ConfigureAwait(false);
                    if (!profiles.TryGetValue(id, out var profile)) return new RecordedTerrainState.Pending();
                    var classified = TerrainClassifier.Classify(profile, scale, TerrainLabelSet.None);
                    return new RecordedTerrainState.Loaded(current.Key, classified, RecordedSessionPresentationDeriver.CreateTerrain(classified));
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
                if (!token.IsCancellationRequested) actions.SetTerrain(current, scale, result);
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
