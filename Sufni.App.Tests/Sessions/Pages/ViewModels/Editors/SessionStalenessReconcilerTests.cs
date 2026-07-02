using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Coordination;
using Sufni.App.Sessions.Pages.ViewModels.Editors;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Doubles;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Sessions.Pages.ViewModels.Editors;

public class SessionStalenessReconcilerTests
{
    [Fact]
    public async Task HandleStalenessAsync_RequestsRecompute_WhenStaleRecomputableAndConfirmed()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Recompute");
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
    public async Task HandleStalenessAsync_RecomputesAll_WhenUserChoosesRecomputeAll()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Recompute all");
        harness.RunProgressWorkInline();
        harness.SessionCoordinator.RequestRecomputeAllAsync(Arg.Any<IProgress<SessionRecomputeAllProgress>>())
            .Returns(new SessionRecomputeAllResult(Total: 3, Recomputed: 3, Superseded: 0, NotRecomputable: 0, Failed: 0));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.StaleOnOpen);

        // The bulk request replaces, not supplements, the single-session recompute,
        // and runs behind the progress dialog.
        await harness.DialogService.Received(1).ShowProgressAsync(
            Arg.Any<string>(),
            Arg.Any<Func<IProgress<DialogProgress>, Task<SessionRecomputeAllResult>>>());
        await harness.SessionCoordinator.Received(1).RequestRecomputeAllAsync(Arg.Any<IProgress<SessionRecomputeAllProgress>>());
        await harness.SessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_SurfacesError_WhenRecomputeAllHasFailures()
    {
        var harness = new ReconcilerHarness();
        harness.ChoosePromptButton("Recompute all");
        harness.RunProgressWorkInline();
        harness.SessionCoordinator.RequestRecomputeAllAsync(Arg.Any<IProgress<SessionRecomputeAllProgress>>())
            .Returns(new SessionRecomputeAllResult(Total: 4, Recomputed: 2, Superseded: 0, NotRecomputable: 1, Failed: 1));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.DependencyHashChanged()),
            RecomputeReason.StaleOnOpen);

        Assert.Contains(harness.Errors, error => error.Contains("could not be recomputed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HandleStalenessAsync_DoesNotRecompute_WhenUserDeclines()
    {
        var harness = new ReconcilerHarness();
        harness.ChoosePromptButton("Cancel");

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

        await harness.DialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await harness.SessionCoordinator.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>(), Arg.Any<RecomputeReason>());
    }

    [Fact]
    public async Task HandleStalenessAsync_DoesNotPrompt_WhenNotStale()
    {
        var harness = new ReconcilerHarness();

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.Current()),
            RecomputeReason.DependencyChanged);

        await harness.DialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
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
        await harness.DialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
    }

    [Fact]
    public async Task HandleStalenessAsync_SurfacesError_WhenRecomputeFails()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Recompute");
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
        harness.ChoosePromptButton("Recompute");
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
        harness.ChoosePromptButton("Recompute");
        harness.SessionCoordinator.RequestRecomputeAsync(sessionId, Arg.Any<RecomputeReason>())
            .Returns(new SessionRecomputeResult.Recomputed(12));
        var domain = Domain(
            TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"),
            new SessionStaleness.DependencyHashChanged());

        await harness.Reconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);
        await harness.Reconciler.HandleStalenessAsync(domain, RecomputeReason.StaleOnOpen);

        await harness.DialogService.Received(1).ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
    }

    [Fact]
    public async Task HandleStalenessAsync_PromptsAgain_WhenProcessingOptionOnlyFingerprintChanges()
    {
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Cancel");
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

        await harness.DialogService.Received(2).ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
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

        // Resolves the recompute prompt as if the user clicked the button with the
        // given label, returning that choice's id (the generic dialog primitive's
        // contract).
        public void ChoosePromptButton(string label) =>
            DialogService
                .ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>())
                .Returns(call => call.Arg<IReadOnlyList<DialogChoice>>().Single(c => c.Label == label).Id);

        // Makes the progress-dialog substitute run the work it is handed (with a
        // no-op progress sink) and return its result, so recompute-all reaches the
        // coordinator during the test.
        public void RunProgressWorkInline() =>
            DialogService
                .ShowProgressAsync(
                    Arg.Any<string>(),
                    Arg.Any<Func<IProgress<DialogProgress>, Task<SessionRecomputeAllResult>>>())
                .Returns(call => call.Arg<Func<IProgress<DialogProgress>, Task<SessionRecomputeAllResult>>>()(
                    new Progress<DialogProgress>()));
    }
}
