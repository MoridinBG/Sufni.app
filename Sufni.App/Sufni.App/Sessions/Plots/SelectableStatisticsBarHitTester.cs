using System;
using System.Collections.Generic;
using ScottPlot;
using Sufni.Telemetry;

using Sufni.App.Theming;
using Sufni.App.Infrastructure.Theming;
namespace Sufni.App.Sessions.Plots;

internal sealed record SelectableStatisticsBarHit(
    Bar Bar,
    TelemetryRangeSelection.BinRange Bin,
    double Value,
    int Index);

internal sealed class SelectableStatisticsBarHitTester
{
    private readonly List<SelectableStatisticsBarHit> hits = [];

    public IReadOnlyList<SelectableStatisticsBarHit> Hits => hits;

    public void Clear() => hits.Clear();

    public void Register(Bar bar, TelemetryRangeSelection.BinRange bin, double value, int index)
    {
        hits.Add(new SelectableStatisticsBarHit(bar, bin, value, index));
    }

    public bool TryHit(double x, double y, out SelectableStatisticsBarHit hit)
    {
        foreach (var candidate in hits)
        {
            if (candidate.Bar.Orientation != Orientation.Vertical)
            {
                continue;
            }

            var left = candidate.Bar.Position - candidate.Bar.Size / 2.0;
            var right = candidate.Bar.Position + candidate.Bar.Size / 2.0;
            var bottom = Math.Min(candidate.Bar.ValueBase, candidate.Bar.Value);
            var top = Math.Max(candidate.Bar.ValueBase, candidate.Bar.Value);
            if (x >= left &&
                x <= right &&
                y >= bottom &&
                y <= top)
            {
                hit = candidate;
                return true;
            }
        }

        hit = default!;
        return false;
    }

    public bool TryHitVerticalBarAtX(double x, out SelectableStatisticsBarHit hit)
    {
        foreach (var candidate in hits)
        {
            if (candidate.Bar.Orientation != Orientation.Vertical ||
                !HasVisibleHeight(candidate.Bar))
            {
                continue;
            }

            var left = candidate.Bar.Position - candidate.Bar.Size / 2.0;
            var right = candidate.Bar.Position + candidate.Bar.Size / 2.0;
            if (x >= left && x <= right)
            {
                hit = candidate;
                return true;
            }
        }

        hit = default!;
        return false;
    }

    private static bool HasVisibleHeight(Bar bar) =>
        Math.Abs(bar.Value - bar.ValueBase) > double.Epsilon;
}

internal static class SelectableStatisticsBarSelection
{
    public static void ApplySelectedRangeSelection(
        IEnumerable<SelectableStatisticsBarHit> hits,
        Color defaultLineColor,
        SufniPlotTheme plotTheme,
        Func<TelemetryRangeSelection.BinRange, bool> isSelectedBin)
    {
        foreach (var hit in hits)
        {
            ApplyPrimaryBarOutline(hit.Bar, defaultLineColor, plotTheme, isSelectedBin(hit.Bin));
        }
    }

    public static void ApplyPrimaryBarOutline(
        Bar bar,
        Color defaultLineColor,
        SufniPlotTheme plotTheme,
        bool isSelected)
    {
        if (isSelected)
        {
            bar.LineColor = plotTheme.Marker.DampingSelectionOutline.ToScottPlotColor();
            bar.LineWidth = 3.0f;
            return;
        }

        bar.LineColor = defaultLineColor;
        bar.LineWidth = 1.5f;
    }

    public static bool MatchesSelectedBin(
        TelemetryRangeSelection.BinRange selected,
        TelemetryRangeSelection.BinRange current)
    {
        const double tolerance = 1e-9;
        return selected.Index == current.Index &&
               selected.IsFirst == current.IsFirst &&
               selected.IsLast == current.IsLast &&
               Math.Abs(selected.Start - current.Start) <= tolerance &&
               Math.Abs(selected.End - current.End) <= tolerance;
    }
}
