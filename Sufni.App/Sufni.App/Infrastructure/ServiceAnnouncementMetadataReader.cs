using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Sufni.App.Infrastructure;

public static class ServiceAnnouncementMetadataReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string? ReadInstanceName(object source)
    {
        foreach (var propertyName in new[] { "InstanceName", "Name", "Instance" })
        {
            if (TryGetPropertyValue(source, propertyName, out var value) &&
                value is string instanceName &&
                !string.IsNullOrWhiteSpace(instanceName))
            {
                return instanceName;
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> ReadTxtRecords(object source)
    {
        var records = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var propertyName in new[] { "TxtRecords", "TxtRecord", "Txt", "TextRecords", "Text", "Metadata" })
        {
            if (!TryGetPropertyValue(source, propertyName, out var value) || value is null)
            {
                continue;
            }

            AppendTxtRecords(value, records, depth: 0);
            if (records.Count > 0)
            {
                return records;
            }
        }

        return records;
    }

    private static void AppendTxtRecords(object value, Dictionary<string, string> records, int depth)
    {
        switch (value)
        {
            case string text:
                AddTextRecord(text, records);
                return;
            case byte[] bytes:
                AddByteRecord(bytes, records);
                return;
            case IDictionary dictionary:
                foreach (DictionaryEntry entry in dictionary)
                {
                    AddKeyValue(entry.Key, entry.Value, records);
                }

                return;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    if (item is not null)
                    {
                        AppendTxtItem(item, records, depth);
                    }
                }

                return;
        }

        if (depth > 1)
        {
            return;
        }

        foreach (var propertyName in new[] { "TxtRecords", "TxtRecord", "Txt", "TextRecords", "Text" })
        {
            if (TryGetPropertyValue(value, propertyName, out var nestedValue) && nestedValue is not null)
            {
                AppendTxtRecords(nestedValue, records, depth + 1);
            }
        }
    }

    private static void AppendTxtItem(object item, Dictionary<string, string> records, int depth)
    {
        switch (item)
        {
            case string text:
                AddTextRecord(text, records);
                return;
            case byte[] bytes:
                AddByteRecord(bytes, records);
                return;
        }

        if (TryGetPropertyValue(item, "Key", out var key) &&
            TryGetPropertyValue(item, "Value", out var value))
        {
            AddKeyValue(key, value, records);
            return;
        }

        AppendTxtRecords(item, records, depth + 1);
    }

    private static void AddKeyValue(object? keySource, object? valueSource, Dictionary<string, string> records)
    {
        var key = keySource?.ToString();
        if (string.IsNullOrEmpty(key) || records.ContainsKey(key))
        {
            return;
        }

        var value = valueSource switch
        {
            null => string.Empty,
            byte[] bytes => DecodeUtf8(bytes) ?? string.Empty,
            _ => valueSource.ToString() ?? string.Empty,
        };
        records.Add(key, value);
    }

    private static void AddTextRecord(string text, Dictionary<string, string> records)
    {
        var separator = text.IndexOf('=');
        var key = separator >= 0 ? text[..separator] : text;
        if (string.IsNullOrEmpty(key) || records.ContainsKey(key))
        {
            return;
        }

        records.Add(key, separator >= 0 ? text[(separator + 1)..] : string.Empty);
    }

    private static void AddByteRecord(byte[] bytes, Dictionary<string, string> records)
    {
        var separator = Array.IndexOf(bytes, (byte)'=');
        var keyBytes = separator >= 0 ? bytes.AsSpan(0, separator) : bytes.AsSpan();
        var key = DecodeUtf8(keyBytes);
        if (string.IsNullOrEmpty(key) || records.ContainsKey(key))
        {
            return;
        }

        var valueBytes = separator >= 0 ? bytes.AsSpan(separator + 1) : ReadOnlySpan<byte>.Empty;
        records.Add(key, DecodeUtf8(valueBytes) ?? string.Empty);
    }

    private static bool TryGetPropertyValue(object source, string propertyName, out object? value)
    {
        value = null;
        var property = source.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null)
        {
            return false;
        }

        value = property.GetValue(source);
        return true;
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
