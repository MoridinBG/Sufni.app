using System.Text;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.Infrastructure;

public class ServiceAnnouncementMetadataReaderTests
{
    [Fact]
    public void ReadTxtRecords_ParsesStringRecords_AndKeepsFirstDuplicate()
    {
        string[] source = ["live_proto=3", "bid=0123456789ABCDEF", "live_proto=2", "empty", "=ignored", "token=a=b"];

        var records = ServiceAnnouncementMetadataReader.ReadTxtRecords(source);

        Assert.Equal("3", records["live_proto"]);
        Assert.Equal("", records["empty"]);
        Assert.Equal("a=b", records["token"]);
        Assert.False(records.ContainsKey(""));
        Assert.Equal("0123456789ABCDEF", records["bid"]);
    }

    [Fact]
    public void ReadTxtRecords_UsesEmptyValueForInvalidUtf8_AndKeepsFirstDuplicate()
    {
        KeyValuePair<string, byte[]>[] source =
        [
            new("invalid", [0xC3, 0x28]),
            new("bid", Encoding.UTF8.GetBytes("0123456789ABCDEF")),
            new("live_proto", Encoding.UTF8.GetBytes("3")),
            new("live_proto", Encoding.UTF8.GetBytes("2")),
        ];

        var records = ServiceAnnouncementMetadataReader.ReadTxtRecords(source);

        Assert.Equal("3", records["live_proto"]);
        Assert.Equal("", records["invalid"]);
        Assert.Equal("0123456789ABCDEF", records["bid"]);
    }

}
