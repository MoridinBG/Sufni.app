using System.Runtime.InteropServices;
using Sufni.App.Acquisition.Models;

namespace Sufni.App.Tests.Acquisition.Models;

public class TelemetryFileSourceTests
{
    [Fact]
    public void TakeOwnership_ExposesOnlyLogicalBytesAndReleasesBackingArray()
    {
        using var memory = new MemoryStream(capacity: 32);
        memory.Write([1, 2, 3, 4, 5]);
        var backingBytes = memory.GetBuffer();

        var source = TelemetryFileSource.TakeOwnership("ride.SST", memory);

        Assert.Equal("ride.SST", source.FileName);
        Assert.Equal(5, source.LogicalLength);
        Assert.Equal(32, source.AllocatedCapacity);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, source.SstBytes.ToArray());
        Assert.True(MemoryMarshal.TryGetArray(source.SstBytes, out var sourceBuffer));
        Assert.Same(backingBytes, sourceBuffer.Array);

        source.Dispose();
        source.Dispose();

        Assert.Equal(0, source.AllocatedCapacity);
        Assert.Throws<ObjectDisposedException>(() => source.SstBytes);
    }
}
