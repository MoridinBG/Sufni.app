using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.LiveDaq.ViewModels.Editors;
using Sufni.App.LiveDaq.Views.Editors;
using Sufni.App.LiveDaq.Views.Shared;
using Sufni.App.Tests.TestSupport.Harness;
using Sufni.App.Tests.TestSupport.LiveDaq;

namespace Sufni.App.Tests.LiveDaq.Views.Editors;

[Collection("Ui")]
public class LiveDaqDetailViewTests
{
    [AvaloniaFact]
    public async Task LiveDaqDetailView_RendersNavigationAndManagementContent()
    {
        var harness = new LiveDaqDetailHarness();
        var editor = harness.CreateEditor();

        await using var mounted = await MountAsync(editor);

        Assert.NotNull(mounted.View.FindControl<Button>("BackButton"));
        Assert.NotNull(mounted.View.FindControl<Button>("StartSessionButton"));
        Assert.NotNull(mounted.View.FindControl<LiveDaqDeviceManagementCard>("DeviceManagementCard"));
        Assert.NotNull(mounted.View.FindControl<ScrollViewer>("DiagnosticsScrollViewer"));
    }

    private static async Task<MountedLiveDaqDetailView> MountAsync(LiveDaqDetailViewModel editor)
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var view = new LiveDaqDetailView
        {
            DataContext = editor
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        return new MountedLiveDaqDetailView(host, view);
    }
}

internal sealed class MountedLiveDaqDetailView(Window host, LiveDaqDetailView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public LiveDaqDetailView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
