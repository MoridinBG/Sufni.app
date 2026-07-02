using System.Text;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.Infrastructure;

public class ServiceAnnouncementMetadataReaderTests
{
    [Fact]
    public void ReadInstanceName_UsesFirstAvailableNameProperty()
    {
        var source = new NamedAnnouncement("daq-1");

        var instanceName = ServiceAnnouncementMetadataReader.ReadInstanceName(source);

        Assert.Equal("daq-1", instanceName);
    }

    [Fact]
    public void ReadTxtRecords_ParsesStringRecords_AndKeepsFirstDuplicate()
    {
        var source = new StringTxtAnnouncement(["live_proto=3", "bid=0123456789ABCDEF", "live_proto=2"]);

        var records = ServiceAnnouncementMetadataReader.ReadTxtRecords(source);

        Assert.Equal("3", records["live_proto"]);
        Assert.Equal("0123456789ABCDEF", records["bid"]);
    }

    [Fact]
    public void ReadTxtRecords_ParsesByteRecords_AndUsesEmptyValueForInvalidUtf8Value()
    {
        var source = new ByteTxtAnnouncement(
        [
            Encoding.UTF8.GetBytes("live_proto=3"),
            new byte[] { (byte)'b', (byte)'i', (byte)'d', (byte)'=', 0xC3, 0x28 },
        ]);

        var records = ServiceAnnouncementMetadataReader.ReadTxtRecords(source);

        Assert.Equal("3", records["live_proto"]);
        Assert.Equal("", records["bid"]);
    }

    [Fact]
    public void ServiceAnnouncement_AddressPortConstructor_DefaultsMetadata()
    {
        var announcement = new ServiceAnnouncement(System.Net.IPAddress.Loopback, 5575);

        Assert.Null(announcement.InstanceName);
        Assert.Empty(announcement.TxtRecords);
    }

    private sealed record NamedAnnouncement(string Name);

    private sealed record StringTxtAnnouncement(IReadOnlyList<string> Txt);

    private sealed record ByteTxtAnnouncement(IReadOnlyList<byte[]> Txt);
}
