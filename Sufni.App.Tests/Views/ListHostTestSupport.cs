using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels;
using Sufni.App.Views;

namespace Sufni.App.Tests.Views;

internal static class ListHostTestSupport
{
    public static async Task<MountedInMainPagesHost<TControl>> MountInSharedMainPagesHostAsync<TControl>(TControl control)
        where TControl : Control
    {
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates(isDesktop: false);

        var shell = MainPagesViewModelTestFactory.Create();
        var hostView = new MainPagesViewBase
        {
            DataContext = shell,
            Content = control,
        };

        var host = await ViewTestHelpers.ShowViewAsync(hostView);
        return new MountedInMainPagesHost<TControl>(host, hostView, control, shell);
    }
}

internal sealed class MountedInMainPagesHost<TControl>(Window host, MainPagesViewBase shellView, TControl control, MainPagesViewModel shellViewModel) : IAsyncDisposable
    where TControl : Control
{
    public Window Host { get; } = host;
    public MainPagesViewBase ShellView { get; } = shellView;
    public TControl Control { get; } = control;
    public MainPagesViewModel ShellViewModel { get; } = shellViewModel;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}