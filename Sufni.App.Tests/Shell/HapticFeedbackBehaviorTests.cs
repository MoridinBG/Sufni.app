using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Sufni.App.Shell.Behaviors;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Shell;

[Collection("Ui")]
public class HapticFeedbackBehaviorTests
{
    [AvaloniaFact]
    public async Task LongPressFeedback_InvokesInheritedCallback()
    {
        var feedbackCount = 0;
        var child = new Border();
        var root = new Grid
        {
            Children = { child },
        };
        HapticFeedbackBehavior.SetLongPressFeedback(root, () => feedbackCount++);
        HapticFeedbackBehavior.SetIsEnabled(child, true);
        var window = await ViewTestHelpers.ShowViewAsync(root);

        try
        {
            child.RaiseEvent(new RoutedEventArgs(
                HapticFeedbackBehavior.LongPressFeedbackRequestedEvent));

            Assert.Equal(1, feedbackCount);
        }
        finally
        {
            window.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}
