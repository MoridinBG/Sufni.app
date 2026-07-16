using Sufni.App.Bikes.Services;

namespace Sufni.App.Tests.Bikes.Services;

public class AdcTravelLookupTableTests
{
    [Fact]
    public void MeasurementToTravel_CachesTheAdcDomainExactly_AndFallsBackOutsideIt()
    {
        var fallbackCalls = 0;
        double Fallback(ushort measurement)
        {
            fallbackCalls++;
            return Math.Sin(measurement * 0.125) * 123.456;
        }

        var table = new AdcTravelLookupTable(Fallback);

        Assert.Equal(AdcTravelLookupTable.EntryCount, fallbackCalls);
        Assert.Equal(AdcTravelLookupTable.EntryCount, table.Count);
        Assert.Equal(32_768, table.RetainedValueBytes);
        for (var measurement = 0; measurement < AdcTravelLookupTable.EntryCount; measurement++)
        {
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(Math.Sin(measurement * 0.125) * 123.456),
                BitConverter.DoubleToInt64Bits(table.MeasurementToTravel((ushort)measurement)));
        }

        Assert.Equal(AdcTravelLookupTable.EntryCount, fallbackCalls);
        foreach (var measurement in new ushort[] { 4096, 32768, ushort.MaxValue })
        {
            var expected = Math.Sin(measurement * 0.125) * 123.456;
            Assert.Equal(
                BitConverter.DoubleToInt64Bits(expected),
                BitConverter.DoubleToInt64Bits(table.MeasurementToTravel(measurement)));
        }

        Assert.Equal(AdcTravelLookupTable.EntryCount + 3, fallbackCalls);
    }
}
