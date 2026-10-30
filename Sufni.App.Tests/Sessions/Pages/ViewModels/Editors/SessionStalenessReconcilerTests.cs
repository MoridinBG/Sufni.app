using Sufni.App.Shared.Models;
using NSubstitute;

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
        harness.RecomputeEngine.RequestRecomputeAsync(sessionId)
            .Returns(new SessionRecomputeResult.Recomputed(12));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"), new SessionStaleness.Stale()));

        await harness.RecomputeEngine.Received(1).RequestRecomputeAsync(sessionId);
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
        harness.RecomputeEngine.RequestRecomputeAllAsync(Arg.Any<IProgress<SessionRecomputeAllProgress>>())
            .Returns(new SessionRecomputeAllResult(Total: 3, Recomputed: 3, Superseded: 0, NotRecomputable: 0, Failed: 0));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"), new SessionStaleness.Stale()));

        // The bulk request replaces, not supplements, the single-session recompute,
        // and runs behind the progress dialog.
        await harness.DialogService.Received(1).ShowProgressAsync(
            Arg.Any<string>(),
            Arg.Any<Func<IProgress<DialogProgress>, Task<SessionRecomputeAllResult>>>());
        await harness.RecomputeEngine.Received(1).RequestRecomputeAllAsync(Arg.Any<IProgress<SessionRecomputeAllProgress>>());
        await harness.RecomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>());
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_SurfacesError_WhenRecomputeAllHasFailures()
    {
        var harness = new ReconcilerHarness();
        harness.ChoosePromptButton("Recompute all");
        harness.RunProgressWorkInline();
        harness.RecomputeEngine.RequestRecomputeAllAsync(Arg.Any<IProgress<SessionRecomputeAllProgress>>())
            .Returns(new SessionRecomputeAllResult(Total: 4, Recomputed: 2, Superseded: 0, NotRecomputable: 1, Failed: 1));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.Stale()));

        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_DoesNotRecompute_WhenUserDeclines()
    {
        var harness = new ReconcilerHarness();
        harness.ChoosePromptButton("Cancel");

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.Stale()));

        await harness.RecomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>());
        Assert.Empty(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_SuppressesPrompt_WhenRecomputeAlreadyActive()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.RecomputeEngine.IsActive(sessionId).Returns(true);

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5), new SessionStaleness.Stale()));

        await harness.DialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await harness.RecomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task HandleStalenessAsync_DoesNotPrompt_WhenNotStale()
    {
        var harness = new ReconcilerHarness();

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(updated: 5), new SessionStaleness.Current()));

        await harness.DialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        Assert.Empty(harness.Errors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleStalenessAsync_ReportsUnavailableInputsOnce(bool missingSource)
    {
        var harness = new ReconcilerHarness();
        var domain = Domain(
            TestSnapshots.Session(updated: 5),
            missingSource
                ? new SessionStaleness.MissingRawSource()
                : new SessionStaleness.MissingDependencies(SetupMissing: true, BikeMissing: false));

        await harness.Reconciler.HandleStalenessAsync(domain);
        await harness.Reconciler.HandleStalenessAsync(domain);

        var error = Assert.Single(harness.Errors);
        Assert.False(string.IsNullOrWhiteSpace(error.Summary));
        await harness.DialogService.DidNotReceive().ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HandleStalenessAsync_SurfacesError_WhenRecomputeFails(bool processing)
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Recompute");
        harness.RecomputeEngine.RequestRecomputeAsync(sessionId)
            .Returns(processing
                ? new SessionRecomputeResult.ProcessingFailed(SessionProcessingFailureReason.InvalidCalibration, "boom")
                : (SessionRecomputeResult)new SessionRecomputeResult.Failed(SessionRecomputeFailureReason.Recompute, "boom"));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5), new SessionStaleness.Stale()));

        var failure = Assert.Single(harness.Errors);
        Assert.Equal("boom", failure.Details);
        Assert.DoesNotContain("boom", failure.Summary);
    }

    [Fact]
    public async Task HandleStalenessAsync_ReportsStale_WhenRecomputeReturnsNotRecomputable()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Recompute");
        harness.RecomputeEngine.RequestRecomputeAsync(sessionId)
            .Returns(new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource(ProcessedStateStale: true)));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(TestSnapshots.Session(id: sessionId, updated: 5), new SessionStaleness.Stale()));

        Assert.Single(harness.Errors);
    }

    [Fact]
    public async Task HandleStalenessAsync_PromptsOncePerSignature()
    {
        var sessionId = Guid.NewGuid();
        var harness = new ReconcilerHarness(sessionId);
        harness.ChoosePromptButton("Recompute");
        harness.RecomputeEngine.RequestRecomputeAsync(sessionId)
            .Returns(new SessionRecomputeResult.Recomputed(12));
        var domain = Domain(
            TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"),
            new SessionStaleness.Stale());

        await harness.Reconciler.HandleStalenessAsync(domain);
        await harness.Reconciler.HandleStalenessAsync(domain);

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
            Domain(session, new SessionStaleness.Stale(), fingerprint));
        await harness.Reconciler.HandleStalenessAsync(
            Domain(session, new SessionStaleness.Stale(), fingerprint with
            {
                VelocityFilterWindowMilliseconds = 100,
            }));

        await harness.DialogService.Received(2).ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await harness.RecomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>());
    }

    [Fact]
    public async Task HandleStalenessAsync_PromptsAgain_WhenDerivationWindowOnlyFingerprintChanges()
    {
        var sessionId = Guid.NewGuid();
        var setupId = Guid.NewGuid();
        var bikeId = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
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
            25,
            new RecordedSessionDerivationWindow(sourceId, 1, 5));

        await harness.Reconciler.HandleStalenessAsync(
            Domain(session, new SessionStaleness.Stale(), fingerprint));
        await harness.Reconciler.HandleStalenessAsync(
            Domain(session, new SessionStaleness.Stale(), fingerprint with
            {
                DerivationWindow = new RecordedSessionDerivationWindow(sourceId, 2, 5),
            }));

        await harness.DialogService.Received(2).ShowChoiceAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<IReadOnlyList<DialogChoice>>());
        await harness.RecomputeEngine.DidNotReceive().RequestRecomputeAsync(Arg.Any<Guid>());
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
        staleness);

    private sealed class ReconcilerHarness
    {
        public ISessionRecomputeEngine RecomputeEngine { get; } = Substitute.For<ISessionRecomputeEngine>();
        public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
        public TestSessionOperationGateway Gateway { get; } = new();
        public List<FailurePresentation> Errors => Gateway.Errors;
        public SessionStalenessReconciler Reconciler { get; }

        public ReconcilerHarness(Guid? sessionId = null)
        {
            Gateway.SessionId = sessionId ?? Guid.NewGuid();
            Reconciler = new SessionStalenessReconciler(RecomputeEngine, DialogService, Gateway);
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
