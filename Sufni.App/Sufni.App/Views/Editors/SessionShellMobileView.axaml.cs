using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Editors;

public partial class SessionShellMobileView : UserControl
{
    public static readonly StyledProperty<ICommand?> LoadedCommandProperty =
        AvaloniaProperty.Register<SessionShellMobileView, ICommand?>(nameof(LoadedCommand));

    public static readonly StyledProperty<ICommand?> UnloadedCommandProperty =
        AvaloniaProperty.Register<SessionShellMobileView, ICommand?>(nameof(UnloadedCommand));

    public static readonly StyledProperty<Control?> ControlContentProperty =
        AvaloniaProperty.Register<SessionShellMobileView, Control?>(nameof(ControlContent));

    public ICommand? LoadedCommand
    {
        get => GetValue(LoadedCommandProperty);
        set => SetValue(LoadedCommandProperty, value);
    }

    public ICommand? UnloadedCommand
    {
        get => GetValue(UnloadedCommandProperty);
        set => SetValue(UnloadedCommandProperty, value);
    }

    public Control? ControlContent
    {
        get => GetValue(ControlContentProperty);
        set => SetValue(ControlContentProperty, value);
    }

    public SessionShellMobileView()
    {
        InitializeComponent();
    }
}
