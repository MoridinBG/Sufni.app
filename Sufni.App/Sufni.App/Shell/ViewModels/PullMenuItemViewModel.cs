using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Sufni.App.Shell.ViewModels;

public partial class PullMenuItemViewModel(string name, IRelayCommand command, object? parameter = null) : ObservableObject
{
    public string Name { get; set; } = name;
    public object? CommandParameter { get; set; } = parameter;

    [ObservableProperty] public partial bool Selected { get; set; }

    public IRelayCommand Command { get; set; } = command;
}
