using Sufni.App.Acquisition.Models;

namespace Sufni.App.Tests.Acquisition.Models;

public class TelemetryFileSourceTests
{
    [Fact]
    public void Dispose_PreventsReadingSourceBytes_AndCanBeRepeated()
    {
        var source = new TelemetryFileSource("ride.SST", [1, 2, 3, 4, 5]);

        source.Dispose();
        source.Dispose();

        Assert.Throws<ObjectDisposedException>(() => source.SstBytes);
    }
}
