using Sufni.App.ExtensionHost.Contracts.Sync;

using Sufni.App.Extensibility.Sync;
using Sufni.App.SyncAndPairing.Services;
namespace Sufni.App.Tests.Extensibility.Sync;

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

        var batches = await service.CreateBatchesAsync(
            sinceExclusive: 5,
            upperInclusive: 10, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([firstEnvelope, thirdEnvelope], batches);
        Assert.Equal((5, 10), first.CreateWindow);
        Assert.Equal((5, 10), second.CreateWindow);
        Assert.Equal((5, 10), third.CreateWindow);
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
            totalSteps: 6, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(participant.AppliedEnvelopes);
    }

    [Fact]
    public async Task ApplyBatchesAsync_PreparesEveryKnownBatchBeforeApplyingAny()
    {
        var first = new TestSyncParticipant("first");
        var second = new TestSyncParticipant("second") { PrepareError = "invalid second batch" };
        var service = new ExtensionSyncService([first, second]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ApplyBatchesAsync(
                [CreateEnvelope("first"), CreateEnvelope("second")],
                SynchronizationPhase.PullingRemoteChanges,
                currentStep: 2,
                totalSteps: 6, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("invalid second batch", exception.Message);
        Assert.Single(first.PreparedEnvelopes);
        Assert.Single(second.PreparedEnvelopes);
        Assert.Empty(first.AppliedEnvelopes);
        Assert.Empty(second.AppliedEnvelopes);
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
            totalSteps: 6, cancellationToken: TestContext.Current.CancellationToken);

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
                totalSteps: 6, cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal("extension failed", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsDuplicateParticipantExtensionIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ExtensionSyncService(
                [
                    new TestSyncParticipant("duplicate"),
                    new TestSyncParticipant("duplicate"),
                ]));

        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public void Constructor_RejectsBlankParticipantExtensionIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new ExtensionSyncService([new TestSyncParticipant(" ")]));

        Assert.Contains("extension id is required", exception.Message);
    }

    private static ExtensionSyncEnvelope CreateEnvelope(string extensionId) => new(
        extensionId,
        SchemaVersion: 1,
        ContentType: "application/test",
        Payload: [1, 2, 3]);

    private sealed class TestSyncParticipant(string extensionId) : IExtensionSyncParticipant
    {
        public string ExtensionId { get; } = extensionId;
        public (long SinceExclusive, long UpperInclusive)? CreateWindow { get; private set; }
        public ExtensionSyncEnvelope? CreateResult { get; init; }
        public string? PrepareError { get; init; }
        public ExtensionSyncApplyResult ApplyResult { get; init; } = new ExtensionSyncApplyResult.Applied([]);
        public List<ExtensionSyncEnvelope> PreparedEnvelopes { get; } = [];
        public List<ExtensionSyncEnvelope> AppliedEnvelopes { get; } = [];

        public Task<ExtensionSyncEnvelope?> CreateBatchAsync(
            long sinceExclusive,
            long upperInclusive,
            CancellationToken cancellationToken)
        {
            CreateWindow = (sinceExclusive, upperInclusive);
            return Task.FromResult(CreateResult);
        }

        public Task<ExtensionSyncPrepareResult> PrepareBatchAsync(
            ExtensionSyncEnvelope envelope,
            CancellationToken cancellationToken)
        {
            PreparedEnvelopes.Add(envelope);
            ExtensionSyncPrepareResult result = PrepareError is null
                ? new ExtensionSyncPrepareResult.Prepared(new PreparedBatch(envelope))
                : new ExtensionSyncPrepareResult.Failed(PrepareError);
            return Task.FromResult(result);
        }

        public Task<ExtensionSyncApplyResult> ApplyPreparedBatchAsync(
            IExtensionSyncPreparedBatch batch,
            CancellationToken cancellationToken)
        {
            AppliedEnvelopes.Add(((PreparedBatch)batch).Envelope);
            return Task.FromResult(ApplyResult);
        }

        private sealed record PreparedBatch(ExtensionSyncEnvelope Envelope)
            : IExtensionSyncPreparedBatch;
    }
}
