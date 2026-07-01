using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;
using Sufni.App.Shell.Views;
namespace Sufni.App.Shell.Coordinators;

public sealed class MobileNavigationShellHost(IUiThreadDispatcher uiThreadDispatcher)
    : ObservableObject, IMobileNavigationShellHost, IMobileNavigationPageHost
{
    private readonly List<MobileNavigationEntry> logicalStack = [];
    private readonly SemaphoreSlim navigationGate = new(1, 1);
    private readonly System.Threading.Lock syncRoot = new();
    private NavigationPage? attachedNavigationPage;
    private Task queuedOperation = Task.CompletedTask;

    public ViewModelBase CurrentView
    {
        get
        {
            lock (syncRoot)
            {
                return logicalStack.Count > 0
                    ? logicalStack[^1].ViewModel
                    : throw new InvalidOperationException("The mobile navigation root has not been set.");
            }
        }
    }

    public bool CanGoBack
    {
        get
        {
            lock (syncRoot)
            {
                return logicalStack.Count > 1;
            }
        }
    }

    public IReadOnlyList<ViewModelBase> LogicalStack
    {
        get
        {
            lock (syncRoot)
            {
                return logicalStack.Select(static entry => entry.ViewModel).ToArray();
            }
        }
    }

    public void SetRoot(ViewModelBase root)
    {
        ArgumentNullException.ThrowIfNull(root);

        NavigationPage? navigationPage;
        ContentPage[] pages;
        lock (syncRoot)
        {
            logicalStack.Clear();
            logicalStack.Add(CreateEntry(root));
            navigationPage = attachedNavigationPage;
            pages = logicalStack.Select(static entry => entry.Page).ToArray();
        }

        NotifyStackPropertiesChanged();

        if (navigationPage is not null)
        {
            QueueNavigationOperation(() => MaterializeAttachedStackAsync(navigationPage, pages));
        }
    }

    public void Push(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        var entry = CreateEntry(viewModel);
        NavigationPage? navigationPage;
        lock (syncRoot)
        {
            logicalStack.Add(entry);
            navigationPage = attachedNavigationPage;
        }

        NotifyStackPropertiesChanged();

        if (navigationPage is not null)
        {
            QueueNavigationOperation(() => PushAttachedPageAsync(navigationPage, entry.Page));
        }
    }

    public bool Pop()
    {
        NavigationPage? navigationPage;
        lock (syncRoot)
        {
            if (logicalStack.Count <= 1)
            {
                return false;
            }

            logicalStack.RemoveAt(logicalStack.Count - 1);
            navigationPage = attachedNavigationPage;
        }

        NotifyStackPropertiesChanged();

        if (navigationPage is not null)
        {
            QueueNavigationOperation(() => PopAttachedPageAsync(navigationPage));
        }

        return true;
    }

    public bool Close(ViewModelBase viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);

        lock (syncRoot)
        {
            if (logicalStack.Count == 0 || !ReferenceEquals(logicalStack[^1].ViewModel, viewModel))
            {
                return false;
            }
        }

        return Pop();
    }

    public void Attach(NavigationPage navigationPage)
    {
        ArgumentNullException.ThrowIfNull(navigationPage);

        ContentPage[] pages;
        lock (syncRoot)
        {
            attachedNavigationPage = navigationPage;
            pages = logicalStack.Select(static entry => entry.Page).ToArray();
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

    private void NotifyStackPropertiesChanged()
    {
        OnPropertyChanged(nameof(CurrentView));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(LogicalStack));
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

    private async Task PushAttachedPageAsync(NavigationPage navigationPage, ContentPage page)
    {
        if (!IsAttached(navigationPage))
        {
            return;
        }

        await navigationPage.PushAsync(page, null);
    }

    private async Task PopAttachedPageAsync(NavigationPage navigationPage)
    {
        if (!IsAttached(navigationPage))
        {
            return;
        }

        await navigationPage.PopAsync(null);
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
