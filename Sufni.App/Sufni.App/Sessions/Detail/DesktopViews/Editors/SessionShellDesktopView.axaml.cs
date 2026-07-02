using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

using Sufni.App.Infrastructure;
using Sufni.App.Shared.DesktopViews.Controls;
namespace Sufni.App.Sessions.Detail.DesktopViews.Editors;

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

    public static readonly StyledProperty<Control?> SignalsContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(SignalsContent));

    public static readonly StyledProperty<Control?> MediaContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(MediaContent));

    public static readonly StyledProperty<Control?> AnalysisContentProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, Control?>(nameof(AnalysisContent));

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

    public Control? SignalsContent
    {
        get => GetValue(SignalsContentProperty);
        set => SetValue(SignalsContentProperty, value);
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

    public Control? AnalysisContent
    {
        get => GetValue(AnalysisContentProperty);
        set => SetValue(AnalysisContentProperty, value);
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
        SignalsMediaSplit.PropertyChanged += OnSignalsMediaSplitPropertyChanged;
        AnalysisSidebarSplit.PropertyChanged += OnAnalysisSidebarSplitPropertyChanged;
        ApplyMediaColumnWidth();
        ApplyLayoutPreferences();
    }

    private void ApplyMediaColumnWidth()
    {
        SignalsMediaSplit.DefaultSecondLength = new GridLength(NormalizeMediaColumnWidth(MediaColumnWidth));
    }

    private void ApplyLayoutPreferences()
    {
        applyingLayoutPreferences = true;
        try
        {
            ShellRowsSplit.Preferences = LayoutPreferences.DesktopShellRows;
            SignalsMediaSplit.Preferences = HasMediaContent ? LayoutPreferences.DesktopSignalsMediaColumns : null;
            AnalysisSidebarSplit.Preferences = LayoutPreferences.DesktopAnalysisSidebarColumns;
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

    private void OnSignalsMediaSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            UpdateSignalsMediaPreferences(SignalsMediaSplit.Preferences);
        }
    }

    private void OnAnalysisSidebarSplitPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs args)
    {
        if (args.Property == CollapsibleSplitView.PreferencesProperty)
        {
            UpdateAnalysisSidebarPreferences(AnalysisSidebarSplit.Preferences);
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

    private void UpdateSignalsMediaPreferences(SessionPaneGroupPreferences? value)
    {
        if (applyingLayoutPreferences)
        {
            return;
        }

        LayoutPreferences = LayoutPreferences with { DesktopSignalsMediaColumns = HasMediaContent ? value : null };
    }

    private void UpdateAnalysisSidebarPreferences(SessionPaneGroupPreferences? value)
    {
        if (applyingLayoutPreferences)
        {
            return;
        }

        LayoutPreferences = LayoutPreferences with { DesktopAnalysisSidebarColumns = value };
    }

    private static double NormalizeMediaColumnWidth(double width) =>
        double.IsFinite(width) && width > 0 ? width : DefaultMediaColumnWidth;
}
