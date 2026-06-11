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
    public async Task HandleDomainChangedAsync_ReloadsFreshExternalUpdate_WhenEditorIsClean()
    {
        var harness = new ReconcilerHarness();
        var initial = TestSnapshots.Session(updated: 1);
        var updated = initial with { Updated = 2, Description = "external update" };

        await harness.Reconciler.HandleDomainChangedAsync(Domain(initial, DerivedChangeKind.Initial));
        await harness.Reconciler.HandleDomainChangedAsync(Domain(updated, DerivedChangeKind.SessionMetadataChanged));

        Assert.Equal([updated], harness.AppliedSnapshots);
        Assert.Equal(1, harness.LoadRequestCount);
        Assert.Equal(2, harness.HostUpdateCount);
    }

    [Fact]
    public async Task HandleDomainChangedAsync_AsksBeforeReloadingDirtyExternalUpdate()
    {
        var harness = new ReconcilerHarness { IsDirty = true };
        var initial = TestSnapshots.Session(updated: 1);
        var updated = initial with { Updated = 2, Description = "external update" };
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(false);

        await harness.Reconciler.HandleDomainChangedAsync(Domain(initial, DerivedChangeKind.Initial));
        await harness.Reconciler.HandleDomainChangedAsync(Domain(updated, DerivedChangeKind.SessionMetadataChanged));

        Assert.Empty(harness.AppliedSnapshots);
        Assert.Equal(0, harness.LoadRequestCount);
        await harness.DialogService.Received(1).ShowConfirmationAsync(
            "Session changed elsewhere",
            "This session has been updated from another source. Discard your changes and reload?");
    }

    [Fact]
    public async Task HandleDomainChangedAsync_RecomputesConfirmedStaleDerivedChange()
    {
        var sessionId = Guid.NewGuid();
        var recomputed = TestSnapshots.Session(id: sessionId, updated: 12);
        var harness = new ReconcilerHarness(sessionId: sessionId, baselineUpdated: 5);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);
        harness.SessionCoordinator
            .RecomputeAsync(sessionId, 5, Arg.Any<CancellationToken>())
            .Returns(new SessionRecomputeResult.Recomputed(12));
        harness.SessionStore.Get(sessionId).Returns(recomputed);

        await harness.Reconciler.HandleDomainChangedAsync(Domain(
            TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"),
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));

        Assert.Equal(12, harness.BaselineUpdated);
        Assert.Equal([TestSnapshots.Session(id: sessionId, updated: 5, name: "stale"), recomputed], harness.AppliedSnapshots);
        Assert.Equal(1, harness.LoadRequestCount);
        await harness.SessionCoordinator.Received(1)
            .RecomputeAsync(sessionId, 5, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApplyRecomputeResultAsync_ConflictReloads_WhenConfirmed()
    {
        var sessionId = Guid.NewGuid();
        var current = TestSnapshots.Session(id: sessionId, updated: 9);
        var harness = new ReconcilerHarness(sessionId: sessionId);
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns(true);

        await harness.Reconciler.ApplyRecomputeResultAsync(new SessionRecomputeResult.Conflict(current));

        Assert.Equal([current], harness.AppliedSnapshots);
        Assert.Equal(1, harness.LoadRequestCount);
        await harness.DialogService.Received(1).ShowConfirmationAsync(
            "Session changed elsewhere",
            "This session has been updated from another source. Discard your changes and reload?");
    }

    [Fact]
    public async Task ApplyRecomputeResultAsync_NotRecomputableReportsOnlyOnce()
    {
        var harness = new ReconcilerHarness();
        var result = new SessionRecomputeResult.NotRecomputable(new SessionStaleness.MissingRawSource());

        await harness.Reconciler.ApplyRecomputeResultAsync(result);
        await harness.Reconciler.ApplyRecomputeResultAsync(result);

        Assert.Equal(
            ["Session is stale and cannot be recomputed until the source recording is restored."],
            harness.Errors);
    }

    [Fact]
    public async Task HandleDomainChangedAsync_ReportsError_WhenDialogServiceThrows()
    {
        var harness = new ReconcilerHarness();
        harness.DialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>())
            .Returns<bool>(_ => throw new InvalidOperationException("dialog unavailable"));

        var task = harness.Reconciler.HandleDomainChangedAsync(Domain(
            TestSnapshots.Session(updated: 5),
            DerivedChangeKind.Initial,
            new SessionStaleness.DependencyHashChanged()));

        await task;
        Assert.Single(harness.Errors);
        Assert.Empty(harness.AppliedSnapshots);
    }

    private static RecordedSessionDomainSnapshot Domain(
        SessionSnapshot session,
        DerivedChangeKind changeKind,
        SessionStaleness? staleness = null) => new(
        session,
        null,
        null,
        null,
        null,
        null,
        staleness ?? new SessionStaleness.Current(),
        changeKind);

    private sealed class ReconcilerHarness
    {
        public ISessionCoordinator SessionCoordinator { get; } = Substitute.For<ISessionCoordinator>();
        public ISessionStore SessionStore { get; } = Substitute.For<ISessionStore>();
        public IDialogService DialogService { get; } = Substitute.For<IDialogService>();
        public List<SessionSnapshot> AppliedSnapshots { get; } = [];
        public List<string> Errors { get; } = [];
        public int LoadRequestCount { get; private set; }
        public int HostUpdateCount { get; private set; }
        public long BaselineUpdated { get; set; }
        public bool IsDirty { get; set; }
        public bool IsViewLoaded { get; set; } = true;
        public bool ShouldDeferDomainHandling { get; set; }
        public SessionStalenessReconciler Reconciler { get; }

        public ReconcilerHarness(Guid? sessionId = null, long baselineUpdated = 1)
        {
            var id = sessionId ?? Guid.NewGuid();
            BaselineUpdated = baselineUpdated;

            Reconciler = new SessionStalenessReconciler(
                SessionCoordinator,
                SessionStore,
                DialogService,
                () => id,
                () => BaselineUpdated,
                value => BaselineUpdated = value,
                () => IsDirty,
                () => IsViewLoaded,
                () => ShouldDeferDomainHandling,
                snapshot =>
                {
                    AppliedSnapshots.Add(snapshot);
                    return Task.CompletedTask;
                },
                () =>
                {
                    LoadRequestCount++;
                    return Task.CompletedTask;
                },
                () => HostUpdateCount++,
                Errors.Add);
        }
    }
}
