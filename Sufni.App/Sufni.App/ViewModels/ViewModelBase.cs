using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.Services;

namespace Sufni.App.ViewModels
{
    public partial class ViewModelBase : ObservableObject
    {
        private const int Timeout = 3000;

        #region Observable properties

        [ObservableProperty] private bool isPointerOverNotifications;

        public ObservableCollection<string> ErrorMessages { get; } = [];
        public ObservableCollection<string> Notifications { get; } = [];

        #endregion Observable properties
        
        #region Private fields

        private readonly IUiThreadDispatcher uiThreadDispatcher;
        private CancellationTokenSource? notificationTimeoutCancellation;

        private CompositeDisposable? scopedSubscriptions;

        #endregion Private fields

        #region Property change handlers

        partial void OnIsPointerOverNotificationsChanged(bool value)
        {
            // Reset the timer when the pointer leaves the notifications container
            if (value) return;
            RestartNotificationTimeout();
        }

        #endregion Property change handlers

        #region Constructors

        protected ViewModelBase(IUiThreadDispatcher uiThreadDispatcher)
        {
            ArgumentNullException.ThrowIfNull(uiThreadDispatcher);
            this.uiThreadDispatcher = uiThreadDispatcher;

            Notifications.CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;
                RestartNotificationTimeout();
            };
        }

        #endregion Constructors

        protected IUiThreadDispatcher UiThreadDispatcher => uiThreadDispatcher;

        #region Commands

        [RelayCommand]
        private void ClearErrors(object? o)
        {
            ErrorMessages.Clear();
        }

        [RelayCommand]
        private void ClearNotifications(object? o)
        {
            CancelNotificationTimeout();
            Notifications.Clear();
        }

        #endregion Commands

        #region Scoped subscriptions

        // Page-lifetime subscriptions: created lazily on Loaded, disposed
        // on Unloaded. Use this rather than constructor-time subscriptions
        // when the VM is a singleton or its view can be detached/reattached.
        protected void EnsureScopedSubscription(Action<CompositeDisposable> setup)
        {
            if (scopedSubscriptions is not null) return;
            scopedSubscriptions = new CompositeDisposable();
            setup(scopedSubscriptions);
        }

        protected void DisposeScopedSubscriptions()
        {
            scopedSubscriptions?.Dispose();
            scopedSubscriptions = null;
        }

        #endregion Scoped subscriptions

        #region Notification timeout

        private void RestartNotificationTimeout()
        {
            CancelNotificationTimeout();
            notificationTimeoutCancellation = new CancellationTokenSource();
            _ = ClearNotificationsAfterTimeoutAsync(notificationTimeoutCancellation.Token);
        }

        private void CancelNotificationTimeout()
        {
            notificationTimeoutCancellation?.Cancel();
            notificationTimeoutCancellation?.Dispose();
            notificationTimeoutCancellation = null;
        }

        private async Task ClearNotificationsAfterTimeoutAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout, cancellationToken);
                await uiThreadDispatcher.InvokeAsync(() =>
                {
                    if (!IsPointerOverNotifications)
                    {
                        Notifications.Clear();
                    }
                });
            }
            catch (OperationCanceledException)
            {
                // Superseded by a newer notification timeout.
            }
        }

        #endregion Notification timeout
    }
}
