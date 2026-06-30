using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Sufni.App.Shared.Views.Converters;

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
