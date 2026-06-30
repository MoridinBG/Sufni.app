using System;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Sessions.Lists.ViewModels.ItemLists;
namespace Sufni.App.Extensibility.Notifications;

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
