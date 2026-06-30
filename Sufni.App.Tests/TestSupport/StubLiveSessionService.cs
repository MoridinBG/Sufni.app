using System.Reactive.Subjects;

using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.TestSupport;

internal sealed class StubLiveSessionService : ILiveSessionService
{
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshots;
    private readonly ISubject<LiveGraphBatch> graphBatches;

    private StubLiveSessionService(
        LiveSessionPresentationSnapshot initialSnapshot,
        ISubject<LiveGraphBatch>? graphBatches,
        LiveSessionCapturePackage? capturePackage)
    {
        Current = initialSnapshot;
        snapshots = new BehaviorSubject<LiveSessionPresentationSnapshot>(initialSnapshot);
        this.graphBatches = graphBatches ?? new Subject<LiveGraphBatch>();
        CapturePackage = capturePackage;
    }

    public IObservable<LiveSessionPresentationSnapshot> Snapshots => snapshots;
    public IObservable<LiveGraphBatch> GraphBatches => graphBatches;
    public LiveSessionPresentationSnapshot Current { get; private set; }

    public LiveSessionCapturePackage? CapturePackage { get; set; }
    public int EnsureAttachedCallCount { get; private set; }
    public int ResetCaptureCallCount { get; private set; }
    public int PrepareCaptureForSaveCallCount { get; private set; }
    public int DisposeCallCount { get; private set; }

    public static StubLiveSessionService WithDefaultLiveStream(
        LiveSessionPresentationSnapshot? initialSnapshot = null,
        ISubject<LiveGraphBatch>? graphBatches = null,
        LiveSessionCapturePackage? capturePackage = null) =>
        new(initialSnapshot ?? LiveSessionPresentationSnapshot.Empty, graphBatches, capturePackage);

    public void PublishSnapshot(LiveSessionPresentationSnapshot snapshot)
    {
        Current = snapshot;
        snapshots.OnNext(snapshot);
    }

    public void PublishGraphBatch(LiveGraphBatch batch)
    {
        graphBatches.OnNext(batch);
    }

    public Task EnsureAttachedAsync(CancellationToken cancellationToken = default)
    {
        EnsureAttachedCallCount++;
        return Task.CompletedTask;
    }

    public Task ResetCaptureAsync(CancellationToken cancellationToken = default)
    {
        ResetCaptureCallCount++;
        PublishSnapshot(LiveSessionPresentationSnapshot.Empty);
        return Task.CompletedTask;
    }

    public Task<LiveSessionCapturePackage> PrepareCaptureForSaveAsync(CancellationToken cancellationToken = default)
    {
        PrepareCaptureForSaveCallCount++;
        return CapturePackage is null
            ? Task.FromException<LiveSessionCapturePackage>(
                new InvalidOperationException("No live session capture package was configured."))
            : Task.FromResult(CapturePackage);
    }

    public ValueTask DisposeAsync()
    {
        DisposeCallCount++;
        snapshots.OnCompleted();
        graphBatches.OnCompleted();
        return ValueTask.CompletedTask;
    }
}
