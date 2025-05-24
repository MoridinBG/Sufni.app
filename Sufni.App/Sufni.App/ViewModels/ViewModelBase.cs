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
            Debug.Assert(App.Current is not null, "App.Current is not null");
            var isDesktop = App.Current.IsDesktop;
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
            Debug.Assert(App.Current is not null, "App.Current is not null");

            if (!App.Current.IsDesktop) return; // OpenPreviousPage is a no-op on desktop

            var vm = App.Current.Services?.GetService<MainViewModel>();
            Debug.Assert(vm != null, nameof(vm) + " != null");
            vm.OpenPreviousView();
        }
    }
}