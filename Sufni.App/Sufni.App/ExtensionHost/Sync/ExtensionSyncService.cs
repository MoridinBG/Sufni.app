using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services;

namespace Sufni.App.ExtensionHost.Sync;

public sealed class ExtensionSyncService : IExtensionSyncService
{
    private readonly IReadOnlyList<IExtensionSyncParticipant> participants;

    public ExtensionSyncService(IEnumerable<IExtensionSyncParticipant> participants)
    {
        this.participants = participants.ToArray();
    }

    public async Task<List<ExtensionSyncEnvelope>> CreateBatchesAsync(
        long since,
        CancellationToken cancellationToken = default)
    {
        var envelopes = new List<ExtensionSyncEnvelope>();
        foreach (var participant in participants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var envelope = await participant.CreateBatchAsync(since, cancellationToken);
            if (envelope is not null)
            {
                envelopes.Add(envelope);
            }
        }

        return envelopes;
    }

    public async Task<IReadOnlyList<SynchronizationProgressSnapshot>> ApplyBatchesAsync(
        IEnumerable<ExtensionSyncEnvelope> envelopes,
        SynchronizationPhase phase,
        int currentStep,
        int totalSteps,
        CancellationToken cancellationToken = default)
    {
        var participantsById = participants.ToDictionary(
            participant => participant.ExtensionId,
            StringComparer.Ordinal);
        var progress = new List<SynchronizationProgressSnapshot>();

        foreach (var envelope in envelopes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!participantsById.TryGetValue(envelope.ExtensionId, out var participant))
            {
                continue;
            }

            var result = await participant.ApplyBatchAsync(envelope, cancellationToken);
            AddProgress(progress, result.ProgressMessages, phase, currentStep, totalSteps);

            if (result is ExtensionSyncApplyResult.Failed failed)
            {
                throw new InvalidOperationException(failed.ErrorMessage);
            }
        }

        return progress;
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
}

