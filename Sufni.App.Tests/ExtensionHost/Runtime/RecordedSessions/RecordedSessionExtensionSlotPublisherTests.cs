using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHost.Contracts.Presentation;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.ExtensionHost.Runtime.RecordedSessions;

public class RecordedSessionExtensionSlotPublisherTests
{
    [Fact]
    public void Publish_ReplacesSlotFamiliesFromBuilder()
    {
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, new TestUiThreadDispatcher(checkAccess: true));
        var mediaPane = new RecordedSessionMediaPaneContribution(
            "extension",
            "media",
            Order: 10,
            new TestContributionViewModel());
        var contextMenu = new RecordedSessionSignalPlotContextMenuContribution(
            "extension",
            "context",
            Order: 20,
            RecordedSessionBuiltInSignalRow.Travel,
            new TelemetryPlotContextMenuAction("inspect", "Inspect", new RelayCommand(() => { })));
        var analysisTab = new RecordedSessionAnalysisTabContribution(
            "extension",
            "tab",
            Order: 30,
            "Extension tab",
            RequestedIndex: 3,
            new TestContributionViewModel());

        publisher.Publish(builder =>
        {
            builder.MediaPanes.Add(mediaPane);
            builder.SignalPlotContextMenuActions.Add(contextMenu);
            builder.AnalysisTabs.Add(analysisTab);
        });

        Assert.Equal([mediaPane], slots.MediaPanes);
        Assert.Equal([contextMenu], slots.SignalPlotContextMenuActions);
        Assert.Equal([analysisTab], slots.AnalysisTabs);

        publisher.Publish(builder => builder.MediaPanes.Add(mediaPane with { ContributionId = "updated" }));

        Assert.Equal(["updated"], slots.MediaPanes.Select(contribution => contribution.ContributionId));
        Assert.Empty(slots.SignalPlotContextMenuActions);
        Assert.Empty(slots.AnalysisTabs);
    }

    [Fact]
    public void RequestPublish_CoalescesOffUiRequestsAndPublishesLatestBuilder()
    {
        var dispatcher = new TestUiThreadDispatcher(checkAccess: false);
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, dispatcher);
        var first = new RecordedSessionToolbarViewContribution(
            "extension",
            "first",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel());
        var second = first with { ContributionId = "second" };

        publisher.RequestPublish(builder => builder.SignalToolbarViews.Add(first));
        publisher.RequestPublish(builder => builder.SignalToolbarViews.Add(second));

        Assert.Equal(1, dispatcher.PendingPostCount);
        Assert.Empty(slots.SignalToolbarViews);

        dispatcher.RunPendingPosts();

        Assert.Equal([second], slots.SignalToolbarViews);
    }

    [Fact]
    public void RequestPublish_OnUiThreadCancelsOlderQueuedPublish()
    {
        var dispatcher = new TestUiThreadDispatcher(checkAccess: false);
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, dispatcher);
        var queued = new RecordedSessionToolbarViewContribution(
            "extension",
            "queued",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel());
        var current = queued with { ContributionId = "current" };

        publisher.RequestPublish(builder => builder.SignalToolbarViews.Add(queued));
        dispatcher.SetCheckAccess(true);
        publisher.RequestPublish(builder => builder.SignalToolbarViews.Add(current));

        Assert.Equal([current], slots.SignalToolbarViews);

        dispatcher.RunPendingPosts();

        Assert.Equal([current], slots.SignalToolbarViews);
    }

    [Fact]
    public void Clear_CancelsOlderQueuedPublish()
    {
        var dispatcher = new TestUiThreadDispatcher(checkAccess: false);
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, dispatcher);
        var queued = new RecordedSessionToolbarViewContribution(
            "extension",
            "queued",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new TestContributionViewModel());

        publisher.RequestPublish(builder => builder.SignalToolbarViews.Add(queued));
        publisher.Clear();

        dispatcher.RunPendingPosts();

        Assert.Empty(slots.SignalToolbarViews);
    }

    private sealed class TestUiThreadDispatcher(bool checkAccess) : IUiThreadDispatcher
    {
        private readonly Queue<Action> pendingPosts = new();
        private bool checkAccess = checkAccess;

        public int PendingPostCount => pendingPosts.Count;

        public bool CheckAccess() => checkAccess;

        public void SetCheckAccess(bool value)
        {
            checkAccess = value;
        }

        public void Post(Action action)
        {
            pendingPosts.Enqueue(action);
        }

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action) => action();

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());

        public void RunPendingPosts()
        {
            while (pendingPosts.Count > 0)
            {
                pendingPosts.Dequeue()();
            }
        }
    }
}
