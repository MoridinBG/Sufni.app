using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using Sufni.App.Models;

namespace Sufni.App.Plots;

internal sealed class TelemetryLegendInteraction(Plot plot)
{
    private const double HiddenItemOpacity = 0.35;

    private readonly List<RegisteredSource> sources = [];
    private TelemetrySourceVisibilityStore? sourceVisibility;

    public void Register(IPlottable plottable, string rowId, string sourceKey)
    {
        if (sources.Any(source => ReferenceEquals(source.Plottable, plottable)))
        {
            return;
        }

        sources.Add(new RegisteredSource(plottable, rowId, sourceKey));
        if (sourceVisibility is not null)
        {
            plottable.IsVisible = sourceVisibility.IsVisible(rowId, sourceKey);
            EnsureAtLeastOneVisible();
        }
    }

    public void Clear()
    {
        sources.Clear();
    }

    public void Enable(TelemetrySourceVisibilityStore visibilityStore)
    {
        sourceVisibility = visibilityStore;
        plot.Legend.ShowItemsFromHiddenPlottables = true;
        plot.Legend.HiddenItemOpacity = HiddenItemOpacity;

        foreach (var source in sources)
        {
            source.Plottable.IsVisible = visibilityStore.IsVisible(source.RowId, source.SourceKey);
        }

        EnsureAtLeastOneVisible();
    }

    public bool TryToggleAt(Pixel pixel, PixelSize plotSize)
    {
        if (!plot.Legend.IsVisible || sourceVisibility is null || sources.Count == 0)
        {
            return false;
        }

        var source = GetSourceAt(pixel, plotSize);
        if (source is null)
        {
            return false;
        }

        if (source.Plottable.IsVisible && sources.Count(item => item.Plottable.IsVisible) <= 1)
        {
            return false;
        }

        var visible = !source.Plottable.IsVisible;
        source.Plottable.IsVisible = visible;
        sourceVisibility.SetVisible(source.RowId, source.SourceKey, visible);
        return true;
    }

    private RegisteredSource? GetSourceAt(Pixel pixel, PixelSize plotSize)
    {
        using var paint = Paint.NewDisposablePaint();
        var renderRect = GetLegendRenderRect(plotSize);
        var layout = plot.Legend.GetLayout(renderRect.Size, paint);
        var legendRect = layout.LegendRect.AlignedInside(renderRect, plot.Legend.Alignment);
        var itemCount = layout.LegendItems.Length;

        for (var index = 0; index < itemCount; index++)
        {
            var item = layout.LegendItems[index];
            if (item.Plottable is null || !IsPointInLegendRow(pixel, legendRect, index, itemCount))
            {
                continue;
            }

            var source = sources.FirstOrDefault(source => ReferenceEquals(source.Plottable, item.Plottable));
            if (source is not null)
            {
                return source;
            }
        }

        return null;
    }

    private PixelRect GetLegendRenderRect(PixelSize plotSize)
    {
        var dataRect = plot.LastRender.DataRect;
        return dataRect.HasArea
            ? dataRect
            : plotSize.ToPixelRect();
    }

    private void EnsureAtLeastOneVisible()
    {
        if (sourceVisibility is null || sources.Count == 0 || sources.Any(source => source.Plottable.IsVisible))
        {
            return;
        }

        var fallback = sources[0];
        fallback.Plottable.IsVisible = true;
        sourceVisibility.SetVisible(fallback.RowId, fallback.SourceKey, true);
    }

    private static bool IsPointInLegendRow(Pixel pixel, PixelRect legendRect, int index, int itemCount)
    {
        var rowHeight = legendRect.Height / Math.Max(1, itemCount);
        var top = legendRect.Top + rowHeight * index;
        var bottom = index == itemCount - 1
            ? legendRect.Bottom
            : legendRect.Top + rowHeight * (index + 1);

        return pixel.X >= legendRect.Left &&
               pixel.X <= legendRect.Right &&
               pixel.Y >= top &&
               pixel.Y <= bottom;
    }

    private sealed record RegisteredSource(IPlottable Plottable, string RowId, string SourceKey);
}
