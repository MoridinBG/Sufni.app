using NSubstitute;
using Sufni.App.Coordinators;
using Sufni.App.ExtensionHost.Contracts.SessionGraph;
using Sufni.App.SessionGraph;
using Sufni.App.Services;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ViewModels.Editors;

public class SessionStalenessReconcilerTests
{
    [Fact]
    public async Task HandleStalenessAsync_RequestsRecompute_WhenStaleRecomputableAndConfirmed()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        harness.SessionCoordinator.RequestRecomputeAsync(sessionId, Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.Recomputed(12));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.StaleOnOpen);

        await harness.SessionCoordinator.Received(1).RequestRecomputeAsync(sessionId, RecomputeReason.StaleOnOpen);
        // A successful recompute drives the editor through the store/watch reaction,
        // not the prompter; the prompter surfaces no error and applies no snapshot.
        Assert.Empty(harness.Errors);
        Assert.Empty(harness.Gateway.AppliedSnapshots);
    }

    [Fact]
    public async Task HandleStalenessAsync_DoesNotRecompute_WhenUserDeclines()
    {
        var harness = new ReconcilerHarness();
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.StaleOnOpen);

        await harness.SessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_SuppressesPrompt_WhenRecomputeAlreadyActive()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.SessionCoordinator.IsRecomputeActive(sessionId).Returns(true);

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.DependencyChanged);

        await harness.DialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        await harness.SessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [Fact]
    public async Task HandleStalenessAsync_DoesNotPrompt_WhenNotStale()
    {
        var harness = new ReconcilerHarness();

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.Current()),
            RecomputeReason.DependencyChanged);

        await harness.DialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_ReportsStaleOnce_WhenNotRecomputable()
    {
        var harness = new ReconcilerHarness();
        var domain = Domain(
            TestSnapshots.Session(updated: 5),
            new SessionStaleness.MissingDependencies(SetupMissing: true, BikeMissing: false));

        await harness.Reconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);
        await harness.Reconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);

        var error = Assert.Single(harness.Errors);
        Assert.Contains("stale", error, StringComparison.OrdinalIgnoreCase);
        await harness.DialogService.DidNotReceive().ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleStalenessAsync_SurfacesError_WhenRecomputeFails()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        harness.SessionCoordinator.RequestRecomputeAsync(sessionId, Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.Failed("boom"));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.StaleOnOpen);

        Assert.Contains(harness.Errors, error => error.Contains("boom", StringComparison.Ordinal));
    }

    [Fact]
    public async Task HandleStalenessAsync_ReportsStale_WhenRecomputeReturnsNotRecomputable()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        harness.SessionCoordinator.RequestRecomputeAsync(sessionId, Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource(ProcessedStateStale: true)));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.StaleOnOpen);

        Assert.Contains(harness.Errors, error => error.Contains("stale", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleStalenessAsync_PromptsOncePerSignature()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        harness.SessionCoordinator.RequestRecomputeAsync(sessionId, Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.Recomputed(12));
        var domain = Domain(
            TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"),
            new SessionStaleness.DependencyHashChanged());

        await harness.Reconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);
        await harness.Reconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);

        await harness.DialogService.Received(1).ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task HandleStalenessAsync_PromptsAgain_WhenProcessingOptionOnlyFingerprintChanges()
    {
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var session = TestSnapshots.Session(id: sessionId, updated: 5, name: "stale");
        var fingerprint = new ProcessingFingerprint(
            3,
            7,
            setupId,
            bikeId,
            1,
            "dependency",
            "source",
            25);

        await harness.Reconciler.HandleStalenessAsync(
            Domain(session, new SessionStaleness.DependencyHashChanged(), fingerprint),
            RecomputeReason.StaleOnOpen);
        await harness.Reconciler.HandleStalenessAsync(
            Domain(session, new SessionStaleness.DependencyHashChanged(), fingerprint with
            {
                VelocityFilterWindowMilliseconds = 100,
            }),
            RecomputeReason.StaleOnOpen);

        await harness.DialogService.Received(2).ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
        await harness.SessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    private static RecordedSessionDomainSnapshot Domain(
        SessionSnapshot session,
        SessionStaleness staleness,
        ProcessingFingerprint? currentFingerprint = null) => new(
        session,
        null,
        null,
        currentFingerprint,
        null,
        null,
        staleness,
        DerivedChangeKind.None);

    private sealed class ReconcilerHarness
    {
        public ISessionCoordinator SessionCoordinator { get; } = Substitute.For<ISessionCoordinator>();
        public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
        public TestSessionOperationGateway Gateway { get; } = new();
        public List<string> Errors => Gateway.Errors;
        public SessionStalenessReconciler Reconciler { get; }

        public ReconcilerHarness(Guid? sessionId = null)
        {
            Gateway.SessionId = sessionId ?? Guid.NewGuid();
            Reconciler = new SessionStalenessReconciler(SessionCoordinator, DialogService, Gateway);
        }
    }
}
