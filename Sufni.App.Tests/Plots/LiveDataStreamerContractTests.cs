using ScottPlot;

namespace Sufni.App.Tests.Plots;

public class LiveDataStreamerContractTests
{
    [Fact]
    public void DataStreamer_UsesSingleValueAndBatchAppendsWithCircularBufferState()
    {
        var plot = new Plot();
        var streamer = plot.Add.DataStreamer(5);

        streamer.ViewScrollLeft();
        streamer.ManageAxisLimits = true;

        streamer.Add(1);
        streamer.AddRange([2, 3]);

        Assert.Equal(3, streamer.Data.CountTotal);
        Assert.Equal(3, streamer.Data.NextIndex);
        Assert.Equal(2, streamer.Data.NewestIndex);
        Assert.Equal(1d, streamer.Data.Data[0]);
        Assert.Equal(2d, streamer.Data.Data[1]);
        Assert.Equal(3d, streamer.Data.Data[2]);
        Assert.True(double.IsNaN(streamer.Data.Data[3]));
        Assert.True(double.IsNaN(streamer.Data.Data[4]));

        streamer.AddRange([4, 5, 6]);

        Assert.Equal(6, streamer.Data.CountTotal);
        Assert.Equal(1, streamer.Data.NextIndex);
        Assert.Equal(0, streamer.Data.NewestIndex);
        Assert.Equal([6d, 2d, 3d, 4d, 5d], streamer.Data.Data);
        Assert.True(streamer.ManageAxisLimits);
    }
}