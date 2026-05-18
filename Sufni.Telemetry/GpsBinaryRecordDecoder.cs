namespace Sufni.Telemetry;

// Decodes the fixed-size GPS binary record emitted inside SST V4 streams. A
// missing or invalid date means the fix is ignored rather than synthesized.
public static class GpsBinaryRecordDecoder
{
    public const int RecordSize = 46;

    public static GpsRecord? Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != RecordSize)
            throw new ArgumentException($"GPS record must be exactly {RecordSize} bytes.", nameof(bytes));

        var reader = new SstByteReader(bytes);
        var date = reader.ReadUInt32();
        var timeMs = reader.ReadUInt32();
        var latitude = reader.ReadDouble();
        var longitude = reader.ReadDouble();
        var altitude = reader.ReadSingle();
        var speed = reader.ReadSingle();
        var heading = reader.ReadSingle();
        var fixMode = reader.ReadByte();
        var satellites = reader.ReadByte();
        var epe2d = reader.ReadSingle();
        var epe3d = reader.ReadSingle();

        if (!TryCreateTimestamp(date, timeMs, out var timestamp))
            return null;

        return new GpsRecord(
            timestamp,
            latitude,
            longitude,
            altitude,
            speed,
            heading,
            fixMode,
            satellites,
            epe2d,
            epe3d);
    }

    private static bool TryCreateTimestamp(uint date, uint timeMs, out DateTime timestamp)
    {
        timestamp = default;
        if (date == 0)
            return false;

        var year = (int)(date / 10000);
        var month = (int)(date / 100 % 100);
        var day = (int)(date % 100);

        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return false;

        if (day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        timestamp = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(timeMs);
        return true;
    }
}
