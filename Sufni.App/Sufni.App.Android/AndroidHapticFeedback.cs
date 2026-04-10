using Android.Views;
using Sufni.App.Services;

namespace Sufni.App.Android;

public sealed class AndroidHapticFeedback(Window window) : IHapticFeedback
{
    public void Click()
    {
        var activity = window.Context as Activity;
#pragma warning disable CA1416
        activity?.Window?.DecorView?.PerformHapticFeedback(FeedbackConstants.ContextClick);
#pragma warning restore CA1416
    }

    public void LongPress()
    {
        var activity = window.Context as Activity;
        activity?.Window?.DecorView?.PerformHapticFeedback(FeedbackConstants.LongPress);
    }
}