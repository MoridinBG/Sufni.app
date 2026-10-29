using System.Reactive.Subjects;

using Sufni.App.LiveDaq.Services.LiveStreaming;
namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class StubLiveSessionService : ILiveSessionService
{
    private readonly BehaviorSubject<LiveSessionPresentationSnapshot> snapshots;
    private readonly ISubject<LiveSignalBatch> signalBatches;

    private StubLiveSessionService(
        LiveSessionPresentationSnapshot initialSnapshot,
        ISubject<LiveSignalBatch>? signalBatches,
        LiveSessionCapturePackage? capturePackage)
    {
        Current = initialSnapshot;
        snapshots = new BehaviorSubject<LiveSessionPresentationSnapshot>(initialSnapshot);
        this.signalBatches = signalBatches ?? new Subject<LiveSignalBatch>();
        CapturePackage = capturePackage;
    }

    public IObservable<LiveSessionPresentationSnapshot> Snapshots => snapshots;
    public IObservable<LiveSignalBatch> SignalBatches => signalBatches;
    public LiveSessionPresentationSnapshot Current { get; private set; }

    public LiveSessionCapturePackage? CapturePackage { get; set; }

    public static StubLiveSessionService WithDefaultLiveStream(
        LiveSessionPresentationSnapshot? initialSnapshot = null,
        ISubject<LiveSignalBatch>? signalBatches = null,
        LiveSessionCapturePackage? capturePackage = null) =>
        new(initialSnapshot ?? LiveSessionPresentationSnapshot.Empty, signalBatches, capturePackage);

    public void PublishSnapshot(LiveSessionPresentationSnapshot snapshot)
    {
        Current = snapshot;
        snapshots.OnNext(snapshot);
    }

    public void PublishSignalBatch(LiveSignalBatch batch)
    {
        signalBatches.OnNext(batch);
    }

    public Task EnsureAttachedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task ResetCaptureAsync(CancellationToken cancellationToken = default)
    {
        PublishSnapshot(LiveSessionPresentationSnapshot.Empty);
        return Task.CompletedTask;
    }

    public Task<LiveSessionCapturePackage> PrepareCaptureForSaveAsync(CancellationToken cancellationToken = default)
    {
        return CapturePackage is null
            ? Task.FromException<LiveSessionCapturePackage>(
                new InvalidOperationException("No live session capture package was configured."))
            : Task.FromResult(CapturePackage);
    }

    public ValueTask DisposeAsync()
    {
        snapshots.OnCompleted();
        signalBatches.OnCompleted();
        return ValueTask.CompletedTask;
    }
}
