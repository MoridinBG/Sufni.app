using Avalonia;
using Avalonia.Controls;
using Sufni.App.Models;

namespace Sufni.App.DesktopViews.Editors;

public partial class SessionShellDesktopView : UserControl
{
    private bool applyingLayoutPreferences;

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

    public static readonly StyledProperty<SessionLayoutPreferences> LayoutPreferencesProperty =
        AvaloniaProperty.Register<SessionShellDesktopView, SessionLayoutPreferences>(
            nameof(LayoutPreferences),
            defaultValue: SessionLayoutPreferences.Default);

    static SessionShellDesktopView()
    {
        HasMediaContentProperty.Changed.AddClassHandler<SessionShellDesktopView>((view, _) => view.ApplyLayoutPreferences());
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
        SessionSectionGridSizing.AttachColumnReset(
            this.FindControl<GridSplitter>("MediaSplitter"),
            this.FindControl<Grid>("TopLayoutGrid")!,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, GridLength.Auto));
        SessionSectionGridSizing.AttachRowReset(
            this.FindControl<GridSplitter>("TelemetryStatisticsSplitter"),
            this.FindControl<Grid>("RootLayoutGrid")!,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, new GridLength(1, GridUnitType.Star)));
        SessionSectionGridSizing.AttachColumnReset(
            this.FindControl<GridSplitter>("StatisticsSidebarSplitter"),
            this.FindControl<Grid>("BottomLayoutGrid")!,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, new GridLength(400, GridUnitType.Pixel)));
        SessionSectionGridSizing.AttachCommit(
            this.FindControl<GridSplitter>("MediaSplitter"),
            PublishLayoutPreferences);
        SessionSectionGridSizing.AttachCommit(
            this.FindControl<GridSplitter>("TelemetryStatisticsSplitter"),
            PublishLayoutPreferences);
        SessionSectionGridSizing.AttachCommit(
            this.FindControl<GridSplitter>("StatisticsSidebarSplitter"),
            PublishLayoutPreferences);
        ApplyLayoutPreferences();
    }

    private void ApplyLayoutPreferences()
    {
        applyingLayoutPreferences = true;
        try
        {
            ApplyRootRows();
            ApplyTopColumns();
            ApplyBottomColumns();
        }
        finally
        {
            applyingLayoutPreferences = false;
        }
    }

    private void ApplyRootRows()
    {
        var grid = RootLayoutGrid;
        SessionSectionGridSizing.ResetRows(
            grid,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, new GridLength(1, GridUnitType.Star)));
        SessionSectionGridSizing.TryApplyRowRatios(
            grid,
            LayoutPreferences.DesktopShellRows,
            [
                (0, SessionLayoutPaneIds.GraphMediaArea),
                (2, SessionLayoutPaneIds.StatisticsSidebarArea),
            ]);
    }

    private void ApplyTopColumns()
    {
        var grid = TopLayoutGrid;
        SessionSectionGridSizing.ResetColumns(
            grid,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, HasMediaContent ? GridLength.Auto : new GridLength(0)));
        if (!HasMediaContent)
        {
            return;
        }

        SessionSectionGridSizing.TryApplyColumnRatios(
            grid,
            LayoutPreferences.DesktopGraphMediaColumns,
            [
                (0, SessionLayoutPaneIds.Graph),
                (2, SessionLayoutPaneIds.Media),
            ]);
    }

    private void ApplyBottomColumns()
    {
        var grid = BottomLayoutGrid;
        SessionSectionGridSizing.ResetColumns(
            grid,
            (0, new GridLength(1, GridUnitType.Star)),
            (2, new GridLength(400, GridUnitType.Pixel)));
        SessionSectionGridSizing.TryApplyColumnRatios(
            grid,
            LayoutPreferences.DesktopStatisticsSidebarColumns,
            [
                (0, SessionLayoutPaneIds.Statistics),
                (2, SessionLayoutPaneIds.Sidebar),
            ]);
    }

    private void PublishLayoutPreferences()
    {
        if (applyingLayoutPreferences || VisualRoot is null)
        {
            return;
        }

        LayoutPreferences = LayoutPreferences with
        {
            DesktopShellRows = SessionSectionGridSizing.CaptureRatios(
                (SessionLayoutPaneIds.GraphMediaArea, TopLayoutGrid.Bounds.Height),
                (SessionLayoutPaneIds.StatisticsSidebarArea, BottomLayoutGrid.Bounds.Height)),
            DesktopGraphMediaColumns = HasMediaContent
                ? SessionSectionGridSizing.CaptureRatios(
                    (SessionLayoutPaneIds.Graph, GraphHost.Bounds.Width),
                    (SessionLayoutPaneIds.Media, MediaHost.Bounds.Width))
                : null,
            DesktopStatisticsSidebarColumns = SessionSectionGridSizing.CaptureRatios(
                (SessionLayoutPaneIds.Statistics, StatisticsHost.Bounds.Width),
                (SessionLayoutPaneIds.Sidebar, SidebarHost.Bounds.Width)),
        };
    }
}
