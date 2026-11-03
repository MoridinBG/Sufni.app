using Sufni.App.Infrastructure;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;

using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
using Sufni.App.Tests.Shell.ViewModels;
namespace Sufni.App.Tests.TestSupport.Harness;

internal static class ListHostTestSupport
{
    public static async Task<MountedInMainPagesHost<TControl>> MountInSharedMainPagesHostAsync<TControl>(TControl control)
        where TControl : Control
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(UiLayoutProfile.Compact);

        var hostView = new MainPagesViewBase
        {
            Content = control,
        };

        var host = await ViewTestHelpers.ShowViewAsync(hostView);
        return new MountedInMainPagesHost<TControl>(host, control);
    }
}

internal sealed class MountedInMainPagesHost<TControl>(Window host, TControl control) : IAsyncDisposable
    where TControl : Control
{
    public Window Host { get; } = host;
    public TControl Control { get; } = control;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}