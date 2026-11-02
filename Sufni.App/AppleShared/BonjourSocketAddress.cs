using System;
using System.Net;

namespace Sufni.App.AppleShared;

// NSNetService returns Darwin sockaddr bytes, including the IPv6 interface scope.
internal static class BonjourSocketAddress
{
    public static IPAddress? Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2) return null;
        return bytes[1] switch
        {
            2 when bytes.Length >= 16 => new IPAddress(bytes.Slice(4, 4)),
            30 when bytes.Length >= 28 => new IPAddress(bytes.Slice(8, 16), BitConverter.ToUInt32(bytes.Slice(24, 4))),
            _ => null,
        };
    }
}
