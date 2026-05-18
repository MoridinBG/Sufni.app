using System;
using AvaloniaProgressRing;

namespace Sufni.App.Views.Controls;

public class ActivityIndicator : ProgressRing
{
    protected override Type StyleKeyOverride => typeof(ProgressRing);
}
