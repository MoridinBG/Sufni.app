namespace Sufni.Telemetry.Tests;

public class TelemetryTimeSelectionTests
{
    [Fact]
    public void EquivalentIntervalsShareOneKeyAndKeepTheGapAndExclusiveEnd()
    {
        var selection = new TelemetryTimeSelection([new(5, 6), new(2, 3), new(0, 2), new(1, 2.5)]);
        var keys = new Dictionary<TelemetryTimeSelection, int> { [selection] = 7 };
        Assert.Equal(7, keys[new([new(0, 3), new(5, 6)])]);
        Assert.True(selection.Contains(2));
        Assert.False(selection.Contains(3));
        Assert.True(selection.Contains(5));
        Assert.False(selection.Contains(6));
        Assert.False(new TelemetryTimeSelection([]).Contains(0));
    }
}
