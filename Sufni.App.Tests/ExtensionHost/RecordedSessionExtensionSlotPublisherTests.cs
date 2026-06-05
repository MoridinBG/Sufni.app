using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.RecordedSessions;
using Sufni.App.Services;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.ExtensionHost;

public class RecordedSessionExtensionSlotPublisherTests
{
    [Fact]
    public void Publish_ReplacesSlotFamiliesFromBuilder()
    {
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, new TestUiThreadDispatcher(checkAccess: true));
        var mediaPane = new RecordedSessionMediaPaneContribution("extension", "media", Order: 10, new object());
        var contextMenu = new RecordedSessionPlotContextMenuContribution(
            "extension",
            "context",
            Order: 20,
            RecordedSessionBuiltInGraphRow.Travel,
            new TelemetryPlotContextMenuAction("inspect", "Inspect", new RelayCommand(() => { })));

        publisher.Publish(builder =>
        {
            builder.MediaPanes.Add(mediaPane);
            builder.PlotContextMenuActions.Add(contextMenu);
        });

        Assert.Equal([mediaPane], slots.MediaPanes);
        Assert.Equal([contextMenu], slots.PlotContextMenuActions);

        publisher.Publish(builder => builder.MediaPanes.Add(mediaPane with { ContributionId = "updated" }));

        Assert.Equal(["updated"], slots.MediaPanes.Select(contribution => contribution.ContributionId));
        Assert.Empty(slots.PlotContextMenuActions);
    }

    [Fact]
    public void RequestPublish_CoalescesOffUiRequestsAndPublishesLatestBuilder()
    {
        var dispatcher = new TestUiThreadDispatcher(checkAccess: false);
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, dispatcher);
        var first = new RecordedSessionToolbarContribution(
            "extension",
            "first",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new object());
        var second = first with { ContributionId = "second" };

        publisher.RequestPublish(builder => builder.GraphToolbarActions.Add(first));
        publisher.RequestPublish(builder => builder.GraphToolbarActions.Add(second));

        Assert.Equal(1, dispatcher.PendingPostCount);
        Assert.Empty(slots.GraphToolbarActions);

        dispatcher.RunPendingPosts();

        Assert.Equal([second], slots.GraphToolbarActions);
    }

    [Fact]
    public void RequestPublish_OnUiThreadCancelsOlderQueuedPublish()
    {
        var dispatcher = new TestUiThreadDispatcher(checkAccess: false);
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, dispatcher);
        var queued = new RecordedSessionToolbarContribution(
            "extension",
            "queued",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new object());
        var current = queued with { ContributionId = "current" };

        publisher.RequestPublish(builder => builder.GraphToolbarActions.Add(queued));
        dispatcher.SetCheckAccess(true);
        publisher.RequestPublish(builder => builder.GraphToolbarActions.Add(current));

        Assert.Equal([current], slots.GraphToolbarActions);

        dispatcher.RunPendingPosts();

        Assert.Equal([current], slots.GraphToolbarActions);
    }

    [Fact]
    public void Clear_CancelsOlderQueuedPublish()
    {
        var dispatcher = new TestUiThreadDispatcher(checkAccess: false);
        var slots = new RecordedSessionExtensionSlots();
        var publisher = new RecordedSessionExtensionSlotPublisher(slots, dispatcher);
        var queued = new RecordedSessionToolbarContribution(
            "extension",
            "queued",
            Order: 1,
            RecordedSessionToolbarZone.Leading,
            new object());

        publisher.RequestPublish(builder => builder.GraphToolbarActions.Add(queued));
        publisher.Clear();

        dispatcher.RunPendingPosts();

        Assert.Empty(slots.GraphToolbarActions);
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
