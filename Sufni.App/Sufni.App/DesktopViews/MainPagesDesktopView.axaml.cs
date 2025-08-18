using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.VisualTree;
using Sufni.App.Views;

namespace Sufni.App.DesktopViews;

public class ProgressToArcConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        const double radius = 12;
        const double startAngle = -90;
        const double startRad = Math.PI * startAngle / 180;
        const double center = 12;

        var remaining = System.Convert.ToDouble(value);
        var angleDeg = 360 * remaining;
        var endAngle = startAngle - angleDeg;
        var endRad = Math.PI * endAngle / 180;

        var startPoint = new Point(
            center + radius * Math.Cos(startRad),
            center + radius * Math.Sin(startRad));

        var endPoint = new Point(
            center + radius * Math.Cos(endRad),
            center + radius * Math.Sin(endRad));

        var figure = new PathFigure
        {
            StartPoint = new Point(center, center),
            Segments =
            [
                new LineSegment { Point = startPoint },
                new ArcSegment
                {
                    Point = endPoint,
                    Size = new Size(radius, radius),
                    SweepDirection = SweepDirection.CounterClockwise,
                    IsLargeArc = angleDeg > 180
                },
                new LineSegment { Point = new Point(center, center) }
            ],
            IsClosed = true
        };

        return new PathGeometry { Figures = [ figure ] };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public partial class MainPagesDesktopView : MainPagesViewBase
{
    public MainPagesDesktopView()
    {
        InitializeComponent();

        // Allow the pane to close/open on tab header clicks
        var pagesTabItems = PagesMenu.GetLogicalChildren();
        foreach (var item in pagesTabItems)
        {
            var tabItem = item as TabItem;
            Debug.Assert(tabItem is not null);

            tabItem.PointerPressed += (_, _) =>
            {
                var splitView = PagesMenu.FindAncestorOfType<SplitView>();
                Debug.Assert(splitView is not null);

                splitView.IsPaneOpen = tabItem != PagesMenu.SelectedItem || !splitView.IsPaneOpen;
            };
        }
    }
}