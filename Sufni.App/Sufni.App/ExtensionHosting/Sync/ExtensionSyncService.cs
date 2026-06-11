using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Sufni.App.Services;
using Sufni.App.ExtensionHosting.Sync;
using Sufni.App.ExtensionHost.Contracts.Sync;

namespace Sufni.App.ExtensionHosting.Sync;

internal sealed class ExtensionSyncService : IExtensionSyncService
{
    private readonly IReadOnlyList<IExtensionSyncParticipant> participants;
    private readonly IReadOnlyDictionary<string, IExtensionSyncParticipant> participantsById;

    public ExtensionSyncService(IEnumerable<IExtensionSyncParticipant> participants)
    {
        this.participants = participants.ToArray();
        participantsById = CreateParticipantMap(this.participants);
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

    private static IReadOnlyDictionary<string, IExtensionSyncParticipant> CreateParticipantMap(
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

        return participantMap;
    }
}
