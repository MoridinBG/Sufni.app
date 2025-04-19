using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Sufni.App.ViewModels
{
    public partial class ViewModelBase : ObservableObject
    {
        public ObservableCollection<string> ErrorMessages { get; } = [];
        public ObservableCollection<string> Notifications { get; } = [];

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

        [RelayCommand]
        protected static void OpenPage(ViewModelBase view)
        {
            var mainViewModel = App.Current?.Services?.GetService<MainViewModel>();
            Debug.Assert(mainViewModel != null, nameof(mainViewModel) + " != null");

            mainViewModel.OpenView(view);
        }

        [RelayCommand]
        protected static void OpenPreviousPage()
        {
            var mainViewModel = App.Current?.Services?.GetService<MainViewModel>();
            Debug.Assert(mainViewModel != null, nameof(mainViewModel) + " != null");

            mainViewModel.OpenPreviousView();
        }
    }
}