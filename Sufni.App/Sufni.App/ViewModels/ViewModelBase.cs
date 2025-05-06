using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Controls.ApplicationLifetimes;
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
            var isDesktop = App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;
            if (isDesktop)
            {
                var vm = App.Current?.Services?.GetService<MainWindowViewModel>();
                Debug.Assert(vm != null, nameof(vm) + " != null");
                vm.OpenView(view);
            }
            else
            {
                var vm = App.Current?.Services?.GetService<MainViewModel>();
                Debug.Assert(vm != null, nameof(vm) + " != null");
                vm.OpenView(view);
            }
        }

        [RelayCommand]
        protected static void OpenPreviousPage()
        {
            var isDesktop = App.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime;
            if (isDesktop)
            {
                var vm = App.Current?.Services?.GetService<MainWindowViewModel>();
                Debug.Assert(vm != null, nameof(vm) + " != null");
                vm.OpenPreviousView();
            }
            else
            {
                var vm = App.Current?.Services?.GetService<MainViewModel>();
                Debug.Assert(vm != null, nameof(vm) + " != null");
                vm.OpenPreviousView();
            }
        }
    }
}