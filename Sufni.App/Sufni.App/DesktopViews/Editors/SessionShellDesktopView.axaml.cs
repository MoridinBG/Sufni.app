using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.DesktopViews.Editors;

public partial class SessionShellDesktopView : UserControl
{
    public static readonly StyledProperty<bool> HasMediaContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, bool>(nameof(HasMediaContent));

    public static readonly StyledProperty<Control?> GraphContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(GraphContent));

    public static readonly StyledProperty<Control?> MediaContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(MediaContent));

    public static readonly StyledProperty<Control?> StatisticsContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(StatisticsContent));

    public static readonly StyledProperty<Control?> SidebarContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(SidebarContent));

    public static readonly StyledProperty<Control?> ControlContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(ControlContent));

    public Control? GraphContent
    {
        get => GetValue(GraphContentProperty);
        set => SetValue(GraphContentProperty, value);
    }

    public bool HasMediaContent
    {
        get => GetValue(HasMediaContentProperty);
        set => SetValue(HasMediaContentProperty, value);
    }

    public Control? MediaContent
    {
        get => GetValue(MediaContentProperty);
        set => SetValue(MediaContentProperty, value);
    }

    public Control? StatisticsContent
    {
        get => GetValue(StatisticsContentProperty);
        set => SetValue(StatisticsContentProperty, value);
    }

    public Control? SidebarContent
    {
        get => GetValue(SidebarContentProperty);
        set => SetValue(SidebarContentProperty, value);
    }

    public Control? ControlContent
    {
        get => GetValue(ControlContentProperty);
        set => SetValue(ControlContentProperty, value);
    }

    public SessionShellDesktopView()
    {
        InitializeComponent();
    }
}