using System;
using System.Collections.Generic;
using System.Text;

namespace Sufni.App.Infrastructure;

public static class ServiceAnnouncementMetadataReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyDictionary<string, string> ReadTxtRecords(IEnumerable<string> source)
    {
        var records = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var text in source)
        {
            var separator = text.IndexOf('=');
            var key = separator >= 0 ? text[..separator] : text;
            if (!string.IsNullOrEmpty(key))
            {
                records.TryAdd(key, separator >= 0 ? text[(separator + 1)..] : string.Empty);
            }
        }

        return records;
    }

    public static IReadOnlyDictionary<string, string> ReadTxtRecords(
        IEnumerable<KeyValuePair<string, byte[]>> source)
    {
        var records = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in source)
        {
            if (!string.IsNullOrEmpty(record.Key) && !records.ContainsKey(record.Key))
            {
                records.Add(record.Key, DecodeUtf8(record.Value) ?? string.Empty);
            }
        }

        return records;
    }

    private static string? DecodeUtf8(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return null;
        }
    }
}
