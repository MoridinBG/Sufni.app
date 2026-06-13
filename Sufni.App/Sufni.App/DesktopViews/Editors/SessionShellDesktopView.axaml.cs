using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sufni.App.DesktopViews.Controls;
using Sufni.App.Models;

namespace Sufni.App.DesktopViews.Editors;

public partial class SessionShellDesktopView : UserControl
{
    private const double DefaultMediaColumnWidth = 400;

    private bool applyingLayoutPreferences;

    public static readonly StyledProperty<bool> HasMediaContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, bool>(nameof(HasMediaContent));

    public static readonly StyledProperty<double> MediaColumnWidthProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, double>(
            nameof(MediaColumnWidth),
            defaultValue: DefaultMediaColumnWidth);

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

    public static readonly StyledProperty<SessionLayoutPreferences> LayoutPreferencesProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, SessionLayoutPreferences>(
            nameof(LayoutPreferences),
            defaultValue: SessionLayoutPreferences.Default);

    static SessionShellDesktopView()
    {
        HasMediaContentProperty.Changed.AddClassHandler<SessionShellDesktopView>((view, _) => view.ApplyLayoutPreferences());
        MediaColumnWidthProperty.Changed.AddClassHandler<SessionShellDesktopView>((view, _) => view.ApplyMediaColumnWidth());
        LayoutPreferencesProperty.Changed.AddClassHandler<SessionShellDesktopView>((view, _) => view.ApplyLayoutPreferences());
    }

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

    public double MediaColumnWidth
    {
        get => GetValue(MediaColumnWidthProperty);
        set => SetValue(MediaColumnWidthProperty, value);
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

    public SessionLayoutPreferences LayoutPreferences
    {
        get => GetValue(LayoutPreferencesProperty);
        set => SetValue(LayoutPreferencesProperty, value);
    }

    public SessionShellDesktopView()
    {
        InitializeComponent();
        ShellRowsSplit.PropertyChanged += OnShellRowsSplitPropertyChanged;
        GraphMediaSplit.PropertyChanged += OnGraphMediaSplitPropertyChanged;
        StatisticsSidebarSplit.PropertyChanged += OnStatisticsSidebarSplitPropertyChanged;
        ApplyMediaColumnWidth();
        ApplyLayoutPreferences();
    }

    private void ApplyMediaColumnWidth()
    {
        GraphMediaSplit.DefaultSecondLength = new GridLength(NormalizeMediaColumnWidth(MediaColumnWidth));
    }

    private void ApplyLayoutPreferences()
    {
        applyingLayoutPreferences = true;
        try
        {
            ShellRowsSplit.Preferences = LayoutPreferences.DesktopShellRows;
            GraphMediaSplit.Preferences = HasMediaContent ? LayoutPreferences.DesktopGraphMediaColumns : null;
            StatisticsSidebarSplit.Preferences = LayoutPreferences.DesktopStatisticsSidebarColumns;
        }
        finally
        {
            applyingLayoutPreferences = false;
        }
    }

    private void OnShellRowsSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            UpdateShellRowsPreferences(ShellRowsSplit.Preferences);
        }
    }

    private void OnGraphMediaSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            UpdateGraphMediaPreferences(GraphMediaSplit.Preferences);
        }
    }

    private void OnStatisticsSidebarSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            UpdateStatisticsSidebarPreferences(StatisticsSidebarSplit.Preferences);
        }
    }

    private void UpdateShellRowsPreferences(SessionPaneGroupPreferences? value)
    {
        if (applyingLayoutPreferences)
        {
            return;
        }

        LayoutPreferences = LayoutPreferences with { DesktopShellRows = value };
    }

    private void UpdateGraphMediaPreferences(SessionPaneGroupPreferences? value)
    {
        if (applyingLayoutPreferences)
        {
            return;
        }

        LayoutPreferences = LayoutPreferences with { DesktopGraphMediaColumns = HasMediaContent ? value : null };
    }

    private void UpdateStatisticsSidebarPreferences(SessionPaneGroupPreferences? value)
    {
        if (applyingLayoutPreferences)
        {
            return;
        }

        LayoutPreferences = LayoutPreferences with { DesktopStatisticsSidebarColumns = value };
    }

    private static double NormalizeMediaColumnWidth(double width) =>
        double.IsFinite(width) && width > 0 ? width : DefaultMediaColumnWidth;
}
