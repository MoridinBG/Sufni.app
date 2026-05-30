using System;
using Avalonia;
using Avalonia.Controls;

namespace Sufni.App.Views.Plots;

public sealed class StatisticsPlotHeaderPanel : Panel
{
    public static readonly StyledProperty<double> GapProperty =
        AvaloniaProperty.Register<StatisticsPlotHeaderPanel, double>(nameof(Gap), 8);

    public double Gap
    {
        get => GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var childConstraint = new Size(double.PositiveInfinity, availableSize.Height);
        foreach (var child in Children)
        {
            child.Measure(childConstraint);
        }

        var desiredWidth = Children.Count switch
        {
            0 => 0,
            1 => Children[0].DesiredSize.Width,
            _ => Children[0].DesiredSize.Width + Gap + Children[1].DesiredSize.Width,
        };
        var desiredHeight = 0.0;
        foreach (var child in Children)
        {
            desiredHeight = Math.Max(desiredHeight, child.DesiredSize.Height);
        }

        return new Size(
            double.IsFinite(availableSize.Width) ? Math.Min(availableSize.Width, desiredWidth) : desiredWidth,
            double.IsFinite(availableSize.Height) ? Math.Min(availableSize.Height, desiredHeight) : desiredHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count == 0)
        {
            return finalSize;
        }

        var title = Children[0];
        var headerContent = Children.Count > 1 ? Children[1] : null;
        var headerWidth = headerContent is null
            ? 0
            : Math.Min(headerContent.DesiredSize.Width, finalSize.Width);
        var headerLeft = finalSize.Width - headerWidth;
        var titleRightLimit = headerContent is null
            ? finalSize.Width
            : Math.Max(0, headerLeft - Gap);
        var titleWidth = Math.Min(title.DesiredSize.Width, titleRightLimit);
        var centeredTitleLeft = (finalSize.Width - titleWidth) / 2;
        var latestTitleLeft = titleRightLimit - titleWidth;
        var titleLeft = Math.Max(0, Math.Min(centeredTitleLeft, latestTitleLeft));

        ArrangeVerticallyCentered(title, titleLeft, titleWidth, finalSize.Height);

        if (headerContent is not null)
        {
            ArrangeVerticallyCentered(headerContent, headerLeft, headerWidth, finalSize.Height);
        }

        return finalSize;
    }

    private static void ArrangeVerticallyCentered(Control control, double x, double width, double parentHeight)
    {
        var height = Math.Min(control.DesiredSize.Height, parentHeight);
        var y = Math.Max(0, (parentHeight - height) / 2);
        control.Arrange(new Rect(x, y, width, height));
    }
}
