using Microsoft.Extensions.Time.Testing;
using Sufni.App.Tests.TestSupport.Doubles;

namespace Sufni.App.Tests.Shared.Base;

public class ViewModelBaseTests
{
    [Fact]
    public async Task NewNotification_RestartsThreeSecondVisibilityWindow()
    {
        var time = new FakeTimeProvider();
        var viewModel = new TestViewModel(time);
        var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.Notifications.CollectionChanged += (_, _) =>
        {
            if (viewModel.Notifications.Count == 0) cleared.TrySetResult();
        };
        viewModel.Notifications.Add("first");
        time.Advance(TimeSpan.FromSeconds(2));
        viewModel.Notifications.Add("second");

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(["first", "second"], viewModel.Notifications);
        time.Advance(TimeSpan.FromSeconds(2));

        await cleared.Task.AwaitBoundedAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(viewModel.Notifications);
    }
}
