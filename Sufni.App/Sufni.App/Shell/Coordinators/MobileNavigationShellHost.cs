using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.ViewModels;
using Sufni.App.Shell.Views;
namespace Sufni.App.Shell.Coordinators;

public sealed class MobileNavigationShellHost : IMobileNavigationShellHost, IMobileNavigationPageHost
{
    private readonly ShellWorkspaceViewModel workspace;
    private readonly IUiThreadDispatcher uiThreadDispatcher;
    private readonly Dictionary<ViewModelBase, ContentPage> materializedPages = [];
    private readonly SemaphoreSlim navigationGate = new(1, 1);
    private readonly System.Threading.Lock syncRoot = new();
    private MobileNavigationEntry? rootEntry;
    private NavigationPage? attachedNavigationPage;
    private Task queuedOperation = Task.CompletedTask;

    public MobileNavigationShellHost(
        ShellWorkspaceViewModel workspace,
        IUiThreadDispatcher uiThreadDispatcher)
    {
        this.workspace = workspace;
        this.uiThreadDispatcher = uiThreadDispatcher;
        workspace.PropertyChanged += OnWorkspacePropertyChanged;
    }

    public void SetRoot(ViewModelBase root)
    {
        ArgumentNullException.ThrowIfNull(root);

        NavigationPage? navigationPage;
        ContentPage[] pages;
        lock (syncRoot)
        {
            rootEntry = GetOrCreateEntry(root);
        }

        workspace.CurrentTab = null;

        lock (syncRoot)
        {
            navigationPage = attachedNavigationPage;
            pages = BuildAttachedPages();
        }

        if (navigationPage is not null)
        {
            QueueNavigationOperation(() => MaterializeAttachedStackAsync(navigationPage, pages));
        }
    }

    public void Attach(NavigationPage navigationPage)
    {
        ArgumentNullException.ThrowIfNull(navigationPage);

        ContentPage[] pages;
        lock (syncRoot)
        {
            attachedNavigationPage = navigationPage;
            pages = BuildAttachedPages();
        }

        if (pages.Length > 0)
        {
            QueueNavigationOperation(() => MaterializeAttachedStackAsync(navigationPage, pages));
        }
    }

    public void Detach(NavigationPage navigationPage)
    {
        ArgumentNullException.ThrowIfNull(navigationPage);

        lock (syncRoot)
        {
            if (ReferenceEquals(attachedNavigationPage, navigationPage))
            {
                attachedNavigationPage = null;
            }
        }
    }

    private static MobileNavigationEntry CreateEntry(ViewModelBase viewModel)
    {
        var page = new ContentPage
        {
            Content = viewModel,
            DataContext = viewModel,
            AutomaticallyApplySafeAreaPadding = false
        };

        NavigationPage.SetHasNavigationBar(page, false);
        NavigationPage.SetHasBackButton(page, false);

        return new MobileNavigationEntry(viewModel, page);
    }

    private MobileNavigationEntry GetOrCreateEntry(ViewModelBase viewModel)
    {
        if (!materializedPages.TryGetValue(viewModel, out var page))
        {
            var entry = CreateEntry(viewModel);
            materializedPages[viewModel] = entry.Page;
            return entry;
        }

        return new MobileNavigationEntry(viewModel, page);
    }

    private ContentPage[] BuildAttachedPages()
    {
        if (rootEntry is null)
        {
            return [];
        }

        return workspace.CurrentTab is { } currentTab
            ? [rootEntry.Page, GetOrCreateEntry(currentTab).Page]
            : [rootEntry.Page];
    }

    private void OnWorkspacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
    {
        if (args.PropertyName != nameof(ShellWorkspaceViewModel.CurrentTab))
        {
            return;
        }

        NavigationPage? navigationPage;
        ContentPage[] pages;
        lock (syncRoot)
        {
            navigationPage = attachedNavigationPage;
            pages = BuildAttachedPages();
        }

        if (navigationPage is not null)
        {
            QueueNavigationOperation(() => MaterializeAttachedStackAsync(navigationPage, pages));
        }
    }

    private void QueueNavigationOperation(Func<Task> operation)
    {
        lock (syncRoot)
        {
            queuedOperation = RunQueuedOperationAsync(queuedOperation, operation);
        }
    }

    private async Task RunQueuedOperationAsync(Task previousOperation, Func<Task> operation)
    {
        try
        {
            await previousOperation.ConfigureAwait(false);
        }
        catch
        {
            // Observe earlier navigation failures so later queued operations can still run.
        }

        await navigationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await uiThreadDispatcher.InvokeAsync(operation).ConfigureAwait(false);
        }
        finally
        {
            navigationGate.Release();
        }
    }

    private async Task MaterializeAttachedStackAsync(NavigationPage navigationPage, IReadOnlyList<ContentPage> pages)
    {
        if (pages.Count == 0 || !IsAttached(navigationPage))
        {
            return;
        }

        await navigationPage.ReplaceAsync(pages[0], null);

        for (var index = 1; index < pages.Count; index++)
        {
            if (!IsAttached(navigationPage))
            {
                return;
            }

            await navigationPage.PushAsync(pages[index], null);
        }
    }

    private bool IsAttached(NavigationPage navigationPage)
    {
        lock (syncRoot)
        {
            return ReferenceEquals(attachedNavigationPage, navigationPage);
        }
    }

    private sealed record MobileNavigationEntry(ViewModelBase ViewModel, ContentPage Page);
}
