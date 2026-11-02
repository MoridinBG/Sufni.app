using System.Net;
using Sufni.App.AppleShared;

namespace Sufni.App.Tests.Infrastructure;

public class BonjourSocketAddressTests
{
    [Fact]
    public void Read_DecodesDarwinIpv4Address()
    {
        byte[] address = [16, 2, 6, 21, 192, 168, 1, 17, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal(IPAddress.Parse("192.168.1.17"), BonjourSocketAddress.Read(address));
    }

    [Fact]
    public void Read_PreservesIpv6InterfaceScope()
    {
        byte[] address = [28, 30, 6, 21, 0, 0, 0, 0,
            0xfe, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1,
            7, 0, 0, 0];
        Assert.Equal(IPAddress.Parse("fe80::1%7"), BonjourSocketAddress.Read(address));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 16, 2, 0, 0 })]
    [InlineData(new byte[] { 28, 30, 0, 0 })]
    [InlineData(new byte[] { 16, 1, 0, 0 })]
    public void Read_IgnoresTruncatedAndNonIpAddresses(byte[] address) =>
        Assert.Null(BonjourSocketAddress.Read(address));
}
