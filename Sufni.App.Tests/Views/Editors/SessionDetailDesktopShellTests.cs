using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.DesktopViews.Items;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.Tests.Views.Editors;

public class SessionDetailDesktopShellTests
{
    [AvaloniaFact]
    public async Task SessionDetailDesktopView_RendersRecordedShellContent()
    {
        var editor = new SessionDetailViewModel
        {
            Name = "Recorded Session 01"
        };

        await using var mounted = await MountAsync(editor);

        var controls = mounted.View.GetVisualDescendants().OfType<Control>().ToArray();

        Assert.NotNull(controls.OfType<SessionShellDesktopView>().FirstOrDefault());
        Assert.NotNull(controls.OfType<RecordedSessionGraphDesktopView>().FirstOrDefault());
        Assert.NotNull(controls.OfType<SessionMediaDesktopView>().FirstOrDefault());
        Assert.NotNull(controls.OfType<SessionStatisticsDesktopView>().FirstOrDefault());
        Assert.NotNull(controls.OfType<SessionSidebarDesktopView>().FirstOrDefault());
    }

    private static async Task<MountedSessionDetailDesktopView> MountAsync(SessionDetailViewModel editor)
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        var view = new SessionDetailDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();
        return new MountedSessionDetailDesktopView(host, view);
    }
}

internal sealed class MountedSessionDetailDesktopView(Window host, SessionDetailDesktopView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public SessionDetailDesktopView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}