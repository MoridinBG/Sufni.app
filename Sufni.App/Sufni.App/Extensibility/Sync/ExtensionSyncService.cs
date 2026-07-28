using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Sync;

using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Extensibility.Sync;

internal sealed class ExtensionSyncService : IExtensionSyncService
{
    private readonly IReadOnlyList<IExtensionSyncParticipant> participants;
    private readonly FrozenDictionary<string, IExtensionSyncParticipant> participantsById;

    public ExtensionSyncService(IEnumerable<IExtensionSyncParticipant> participants)
    {
        this.participants = participants.ToArray();
        participantsById = CreateParticipantMap(this.participants);
    }

    public async Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(
        long sinceExclusive,
        long upperInclusive,
        CancellationToken cancellationToken = default)
    {
        var envelopes = new List<ExtensionSyncEnvelope>();
        foreach (var participant in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = await participant.CreateBatchAsync(
                sinceExclusive,
                upperInclusive,
                cancellationToken);
            if (envelope is not null)
            {
                envelopes.Add(envelope);
            }
        }

        return envelopes;
    }

    public async Task<ExtensionSyncApplyPlan> PrepareBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        CancellationToken cancellationToken = default)
    {
        var prepared = new List<PreparedExtensionSyncBatch>();
        foreach (var envelope in envelopes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!participantsById.TryGetValue(envelope.ExtensionId, out var participant))
            {
                continue;
            }

            var result = await participant.PrepareBatchAsync(envelope, cancellationToken);
            if (result is ExtensionSyncPrepareResult.Failed failed)
            {
                throw new InvalidOperationException(failed.ErrorMessage);
            }

            prepared.Add(new PreparedExtensionSyncBatch(
                participant,
                ((ExtensionSyncPrepareResult.Prepared)result).Batch));
        }

        return new ExtensionSyncApplyPlan(prepared);
    }

    public async Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyPreparedBatchesAsync(
        ExtensionSyncApplyPlan plan,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default)
    {
        var progress = new List<SynchronizationProgressSnapshot>();
        foreach (var prepared in plan.Batches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await prepared.Participant.ApplyPreparedBatchAsync(
                prepared.Batch,
                cancellationToken);
            AddProgress(progress, result.ProgressMessages, phase, currentStep, totalSteps);

            if (result is ExtensionSyncApplyResult.Failed failed)
            {
                throw new InvalidOperationException(failed.ErrorMessage);
            }
        }

        return progress;
    }

    public async Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default)
    {
        var plan = await PrepareBatchesAsync(envelopes, cancellationToken);
        return await ApplyPreparedBatchesAsync(
            plan,
            phase,
            currentStep,
            totalSteps,
            cancellationToken);
    }

    private static void AddProgress(
        List<SynchronizationProgressSnapshot> progress,
        IReadOnlyList<string> messages,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps)
    {
        foreach (var message in messages)
        {
            progress.Add(new SynchronizationProgressSnapshot(
                phase,
                message,
                currentStep,
                totalSteps,
                IsDeterminate: totalSteps > 0));
        }
    }

    private static FrozenDictionary<string, IExtensionSyncParticipant> CreateParticipantMap(
        IReadOnlyList<IExtensionSyncParticipant> participants)
    {
        var participantMap = new Dictionary<string, IExtensionSyncParticipant>(StringComparer.Ordinal);
        foreach (var participant in participants)
        {
            if (string.IsNullOrWhiteSpace(participant.ExtensionId))
            {
                throw new InvalidOperationException("Extension sync participant extension id is required.");
            }

            if (!participantMap.TryAdd(participant.ExtensionId, participant))
            {
                throw new InvalidOperationException(
                    $"More than one extension sync participant is registered for extension '{participant.ExtensionId}'.");
            }
        }

        return participantMap.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
