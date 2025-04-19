using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.ViewModels;

public partial class PullMenuItemViewModel(string name, IRelayCommand command, object? parameter = null) : ViewModelBase
{
    public string Name { get; set; } = name;
    public IRelayCommand Command { get; set; } = command;
    public object? CommandParameter { get; set; } = parameter;
    [ObservableProperty] private bool selected;
}
