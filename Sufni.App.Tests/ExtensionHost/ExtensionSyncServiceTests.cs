using Sufni.App.ExtensionHost.Sync;
using Sufni.App.Services;

namespace Sufni.App.Tests.ExtensionHost;

public class ExtensionSyncServiceTests
{
    [Fact]
    public async Task CreateBatchesAsync_SkipsNullBatchesAndPreservesParticipantOrder()
    {
        var firstEnvelope = CreateEnvelope("first");
        var thirdEnvelope = CreateEnvelope("third");
        var first = new TestSyncParticipant("first") { CreateResult = firstEnvelope };
        var second = new TestSyncParticipant("second");
        var third = new TestSyncParticipant("third") { CreateResult = thirdEnvelope };
        var service = new ExtensionSyncService([first, second, third]);

        var batches = await service.CreateBatchesAsync(since: 5);

        Assert.Equal([firstEnvelope, thirdEnvelope], batches);
        Assert.Equal(5, first.CreateSince);
        Assert.Equal(5, second.CreateSince);
        Assert.Equal(5, third.CreateSince);
    }

    [Fact]
    public async Task ApplyBatchesAsync_IgnoresUnknownExtensionIds()
    {
        var participant = new TestSyncParticipant("known");
        var service = new ExtensionSyncService([participant]);

        await service.ApplyBatchesAsync(
            [CreateEnvelope("unknown")],
            SynchronizationPhase.PullingRemoteChanges,
            currentStep: 2,
            totalSteps: 6);

        Assert.Empty(participant.AppliedEnvelopes);
    }

    [Fact]
    public async Task ApplyBatchesAsync_AppliesKnownBatchesAndReturnsProgressSnapshots()
    {
        var envelope = CreateEnvelope("known");
        var participant = new TestSyncParticipant("known")
        {
            ApplyResult = new ExtensionSyncApplyResult.Applied(["Applied extension batch"]),
        };
        var service = new ExtensionSyncService([participant]);

        var progress = await service.ApplyBatchesAsync(
            [envelope],
            SynchronizationPhase.PullingRemoteChanges,
            currentStep: 2,
            totalSteps: 6);

        Assert.Equal([envelope], participant.AppliedEnvelopes);
        var snapshot = Assert.Single(progress);
        Assert.Equal(SynchronizationPhase.PullingRemoteChanges, snapshot.Phase);
        Assert.Equal("Applied extension batch", snapshot.Message);
        Assert.Equal(2, snapshot.CurrentStep);
        Assert.Equal(6, snapshot.TotalSteps);
    }

    [Fact]
    public async Task ApplyBatchesAsync_Throws_WhenKnownParticipantFails()
    {
        var participant = new TestSyncParticipant("known")
        {
            ApplyResult = new ExtensionSyncApplyResult.Failed("extension failed", []),
        };
        var service = new ExtensionSyncService([participant]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyBatchesAsync(
                [CreateEnvelope("known")],
                SynchronizationPhase.PullingRemoteChanges,
                currentStep: 2,
                totalSteps: 6));

        Assert.Equal("extension failed", exception.Message);
    }

    private static ExtensionSyncEnvelope CreateEnvelope(string extensionId) => new(
        extensionId,
        SchemaVersion: 1,
        ContentType: "application/test",
        Payload: [1, 2, 3]);

    private sealed class TestSyncParticipant(string extensionId) : IExtensionSyncParticipant
    {
        public string ExtensionId { get; } = extensionId;
        public long? CreateSince { get; private set; }
        public ExtensionSyncEnvelope? CreateResult { get; init; }
        public ExtensionSyncApplyResult ApplyResult { get; init; } = new ExtensionSyncApplyResult.Applied([]);
        public List<ExtensionSyncEnvelope> AppliedEnvelopes { get; } = [];

        public Task<ExtensionSyncEnvelope?> CreateBatchAsync(long since, CancellationToken cancellationToken)
        {
            CreateSince = since;
            return Task.FromResult(CreateResult);
        }

        public Task<ExtensionSyncApplyResult> ApplyBatchAsync(ExtensionSyncEnvelope envelope, CancellationToken cancellationToken)
        {
            AppliedEnvelopes.Add(envelope);
            return Task.FromResult(ApplyResult);
        }
    }
}

