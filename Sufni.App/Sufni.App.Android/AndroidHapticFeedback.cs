using System;
using Android.App;
using Android.Views;
using Sufni.App.Infrastructure;

namespace Sufni.App.Android;

public sealed class AndroidHapticFeedback(Func<Window?> getWindow) : IHapticFeedback
{
    public void LongPress()
    {
        var activity = getWindow()?.Context as Activity;
        activity?.Window?.DecorView?.PerformHapticFeedback(FeedbackConstants.LongPress);
    }
}
