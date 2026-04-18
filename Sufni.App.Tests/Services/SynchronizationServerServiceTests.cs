using System.Text;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class SynchronizationServerServiceTests
{
    [Fact]
    public void GetJwtSecretKeyBytes_ReturnsUtf8Bytes_ForNonAsciiSecrets()
    {
        var secret = "p" + '\u00E4' + "ssw" + '\u00F6' + "rd";

        var bytes = SynchronizationServerService.GetJwtSecretKeyBytes(secret);

        Assert.Equal(Encoding.UTF8.GetBytes(secret), bytes);
    }
}