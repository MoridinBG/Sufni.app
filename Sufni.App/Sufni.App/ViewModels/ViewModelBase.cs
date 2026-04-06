using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Timers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
        
        // Timer for notifications. They are closed 3 seconds after the last messages arrived,unless
        // the pointer is over them. In such case, the timer starts when the pointer leaves.
        private readonly Timer notificationsTimer = new(Timeout);

        #endregion Private fields

        #region Property change handlers

        partial void OnIsPointerOverNotificationsChanged(bool value)
        {
            // Reset the timer when the pointer leaves the notifications container
            if (value) return;
            notificationsTimer.Stop();
            notificationsTimer.Start();
        }

        #endregion Property change handlers

        #region Constructors

        protected ViewModelBase()
        {
            notificationsTimer.AutoReset = false;
            notificationsTimer.Elapsed += (_, _) =>
            {
                if (IsPointerOverNotifications) return;
                Notifications.Clear();
            };

            Notifications.CollectionChanged += (_, args) =>
            {
                if (args.Action != NotifyCollectionChangedAction.Add) return;
                notificationsTimer.Stop();
                notificationsTimer.Start();
            };
        }

        #endregion Constructors

        #region Commands

        [RelayCommand]
        private void ClearErrors(object? o)
        {
            ErrorMessages.Clear();
        }

        [RelayCommand]
        private void ClearNotifications(object? o)
        {
            Notifications.Clear();
        }

        #endregion Commands
    }
}