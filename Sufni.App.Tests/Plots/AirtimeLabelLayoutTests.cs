using Sufni.App.Plots;

namespace Sufni.App.Tests.Plots;

public class AirtimeLabelLayoutTests
{
    [Fact]
    public void SelectVisibleLabels_WhenLabelsFit_ReturnsEveryVisibleLabel()
    {
        var selected = AirtimeLabelLayout.SelectVisibleLabels(
            [
                new AirtimeLabelLayoutCandidate(0, CenterSeconds: 1, WidthPixels: 40, Priority: 0.3),
                new AirtimeLabelLayoutCandidate(1, CenterSeconds: 5, WidthPixels: 40, Priority: 0.4),
                new AirtimeLabelLayoutCandidate(2, CenterSeconds: 9, WidthPixels: 40, Priority: 0.2),
            ],
            visibleMinimumSeconds: 0,
            visibleMaximumSeconds: 10,
            dataAreaWidthPixels: 500);

        Assert.Equal([true, true, true], selected);
    }

    [Fact]
    public void SelectVisibleLabels_WhenLabelsCollide_KeepsHigherPriorityLabels()
    {
        var selected = AirtimeLabelLayout.SelectVisibleLabels(
            [
                new AirtimeLabelLayoutCandidate(0, CenterSeconds: 1.00, WidthPixels: 80, Priority: 0.2),
                new AirtimeLabelLayoutCandidate(1, CenterSeconds: 1.05, WidthPixels: 80, Priority: 0.5),
                new AirtimeLabelLayoutCandidate(2, CenterSeconds: 8.00, WidthPixels: 80, Priority: 0.1),
            ],
            visibleMinimumSeconds: 0,
            visibleMaximumSeconds: 10,
            dataAreaWidthPixels: 500);

        Assert.Equal([false, true, true], selected);
    }

    [Fact]
    public void SelectVisibleLabels_WhenSessionIsLongButEventsAreSparse_KeepsSeparatedLabels()
    {
        var selected = AirtimeLabelLayout.SelectVisibleLabels(
            [
                new AirtimeLabelLayoutCandidate(0, CenterSeconds: 25, WidthPixels: 75, Priority: 0.4),
                new AirtimeLabelLayoutCandidate(1, CenterSeconds: 35, WidthPixels: 75, Priority: 0.5),
            ],
            visibleMinimumSeconds: 0,
            visibleMaximumSeconds: 42,
            dataAreaWidthPixels: 1900);

        Assert.Equal([true, true], selected);
    }

    [Fact]
    public void SelectVisibleLabels_WhenLabelCenterIsOutsideViewport_HidesLabel()
    {
        var selected = AirtimeLabelLayout.SelectVisibleLabels(
            [
                new AirtimeLabelLayoutCandidate(0, CenterSeconds: 1, WidthPixels: 40, Priority: 0.2),
                new AirtimeLabelLayoutCandidate(1, CenterSeconds: 5, WidthPixels: 40, Priority: 0.2),
                new AirtimeLabelLayoutCandidate(2, CenterSeconds: 9, WidthPixels: 40, Priority: 0.2),
            ],
            visibleMinimumSeconds: 2,
            visibleMaximumSeconds: 8,
            dataAreaWidthPixels: 500);

        Assert.Equal([false, true, false], selected);
    }
}
