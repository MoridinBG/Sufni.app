using System;
using Sufni.App.Services;
using Sufni.App.ViewModels.ItemLists;
using Sufni.App.ExtensionHost.Contracts.Services;
using Sufni.App.ExtensionHosting;

namespace Sufni.App.ExtensionHosting;

internal sealed class ExtensionNotificationService(
    SessionListViewModel sessionsPage,
    IUiThreadDispatcher uiThreadDispatcher) : IExtensionNotificationService
{
    public void AddNotification(string message)
    {
        Post(() => sessionsPage.Notifications.Add(message));
    }

    public void AddError(string message)
    {
        Post(() => sessionsPage.ErrorMessages.Add(message));
    }

    private void Post(Action action)
    {
        if (uiThreadDispatcher.CheckAccess())
        {
            action();
            return;
        }

        uiThreadDispatcher.Post(action);
    }
}
