using System;

namespace Sufni.App;

public static class UuidUtil
{
    public static Guid CreateDeviceUuid(byte[] serial)
    {
        ArgumentNullException.ThrowIfNull(serial);
        if (serial.Length != 8) throw new ArgumentException("Serial must be exactly 8 bytes.", nameof(serial));

        Span<byte> bytes = stackalloc byte[16];

        // Zero first 6 bytes
        for (var i = 0; i < 6; i++)
            bytes[i] = 0;

        // Set version 8 (upper 4 bits of byte 7)
        bytes[6] = (byte)((bytes[6] & 0x0F) | (8 << 4));

        // Store first byte of serial in byte 8
        bytes[7] = serial[0];

        // Set variant (10x...) in byte 9
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);

        // Store remainder of serial
        for (var i = 1; i < 8; i++)
            bytes[8 + i] = serial[i];

        return new Guid(bytes);
    }

    public static Guid CreateDeviceUuid(string serial)
    {
        var serialBytes = Convert.FromHexString(serial);
        return CreateDeviceUuid(serialBytes);
    }
}
