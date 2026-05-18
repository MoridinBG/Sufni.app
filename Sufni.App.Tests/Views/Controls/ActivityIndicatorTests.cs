using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.VisualTree;
using Sufni.App.Views.Controls;

namespace Sufni.App.Tests.Views.Controls;

public class ActivityIndicatorTests
{
    [AvaloniaFact]
    public async Task ActivityIndicator_RendersProgressRingTemplate_WhenActive()
    {
        EnsureProgressRingStyle();
        var indicator = new ActivityIndicator
        {
            Width = 24,
            Height = 24,
            IsActive = true,
        };

        await using var mounted = await MountAsync(indicator);

        Assert.True(mounted.View.IsActive);
        Assert.NotEmpty(mounted.View.GetVisualDescendants());
    }

    private static void EnsureProgressRingStyle()
    {
        var application = Application.Current
            ?? throw new InvalidOperationException("App.Current is null. Did you forget [AvaloniaFact]?");
        var source = new Uri("avares://AvaloniaProgressRing/Styles/ProgressRing.xaml");

        if (application.Styles.OfType<StyleInclude>().Any(style => style.Source?.AbsoluteUri == source.AbsoluteUri))
        {
            return;
        }

        application.Styles.Add(new StyleInclude(new Uri("avares://AvaloniaProgressRing/"))
        {
            Source = source,
        });
    }

    private static async Task<MountedActivityIndicator> MountAsync(ActivityIndicator view)
    {
        var host = await ViewTestHelpers.ShowViewAsync(view);
        view.ApplyTemplate();
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedActivityIndicator(host, view);
    }
}

internal sealed class MountedActivityIndicator(Window host, ActivityIndicator view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public ActivityIndicator View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
