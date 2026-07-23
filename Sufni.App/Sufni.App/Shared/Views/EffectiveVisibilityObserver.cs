using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.VisualTree;

namespace Sufni.App.Shared.Views;

internal sealed class EffectiveVisibilityObserver(
    Visual owner,
    Action<bool> visibilityChanged) : IDisposable
{
    private readonly List<IDisposable> subscriptions = [];
    private bool isAttached;

    public bool IsEffectivelyVisible { get; private set; }

    public void Attach()
    {
        if (isAttached)
        {
            return;
        }

        isAttached = true;
        Subscribe(owner);
        foreach (var ancestor in owner.GetVisualAncestors())
        {
            Subscribe(ancestor);
        }

        Refresh();
    }

    public void Detach()
    {
        if (!isAttached)
        {
            return;
        }

        isAttached = false;
        ClearSubscriptions();
        Publish(false);
    }

    public void Dispose()
    {
        Detach();
    }

    private void Subscribe(Visual visual)
    {
        subscriptions.Add(visual.GetObservable(Visual.IsVisibleProperty).Subscribe(_ => Refresh()));
    }

    private void Refresh()
    {
        Publish(isAttached && owner.IsEffectivelyVisible);
    }

    private void Publish(bool value)
    {
        if (IsEffectivelyVisible == value)
        {
            return;
        }

        IsEffectivelyVisible = value;
        visibilityChanged(value);
    }

    private void ClearSubscriptions()
    {
        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        subscriptions.Clear();
    }
}
