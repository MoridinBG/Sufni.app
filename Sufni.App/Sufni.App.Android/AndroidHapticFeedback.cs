using System;
using Android.App;
using Android.Views;
using Sufni.App.Infrastructure;

namespace Sufni.App.Android;

public sealed class AndroidHapticFeedback(Func<Window?> getWindow) : IHapticFeedback
{
    public void Click()
    {
        var activity = getWindow()?.Context as Activity;
#pragma warning disable CA1416
        activity?.Window?.DecorView?.PerformHapticFeedback(FeedbackConstants.ContextClick);
#pragma warning restore CA1416
    }

    public void LongPress()
    {
        var activity = getWindow()?.Context as Activity;
        activity?.Window?.DecorView?.PerformHapticFeedback(FeedbackConstants.LongPress);
    }
}
