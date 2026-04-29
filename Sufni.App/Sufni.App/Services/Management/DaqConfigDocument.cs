using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Sufni.App.Services.Management;

public sealed class DaqConfigDocument
{
    private readonly IReadOnlyList<DaqConfigLine> lines;
    private readonly IReadOnlyDictionary<string, string> effectiveValues;

    private DaqConfigDocument(
        IReadOnlyList<DaqConfigLine> lines,
        IReadOnlyDictionary<string, string> effectiveValues)
    {
        this.lines = lines;
        this.effectiveValues = effectiveValues;
    }

    public static DaqConfigDocument Parse(byte[] bytes) =>
        Parse(Encoding.UTF8.GetString(bytes));

    public static DaqConfigDocument Parse(string text)
    {
        var values = DaqConfigFields.All.ToDictionary(field => field.Key, field => field.DefaultValue);
        var parsedLines = new List<DaqConfigLine>();

        foreach (var line in SplitLines(text))
        {
            var parsedAssignment = TryParseAssignment(line, out var key, out var value);
            var definition = parsedAssignment && DaqConfigFields.TryGet(key, out var field)
                ? field
                : null;

            if (definition is not null)
            {
                values[definition.Key] = value;
            }

            parsedLines.Add(new DaqConfigLine(line, definition));
        }

        return new DaqConfigDocument(parsedLines, values);
    }

    public IReadOnlyList<DaqConfigFieldValue> GetFieldValues() =>
        DaqConfigFields.All
            .Select(field => new DaqConfigFieldValue(field, GetValue(field.Key)))
            .ToList();

    public string GetValue(string key)
    {
        var definition = DaqConfigFields.Get(key);
        return effectiveValues.TryGetValue(definition.Key, out var value)
            ? value
            : definition.DefaultValue;
    }

    public string BuildText(IReadOnlyDictionary<string, string> values)
    {
        var emitted = new HashSet<string>(StringComparer.Ordinal);
        var output = new List<string>();

        foreach (var line in lines)
        {
            if (line.Definition is null)
            {
                output.Add(line.Text);
            }
            else if (emitted.Add(line.Definition.Key))
            {
                output.Add(BuildAssignment(line.Definition, values));
            }
        }

        foreach (var field in DaqConfigFields.All)
        {
            if (emitted.Add(field.Key))
            {
                output.Add(BuildAssignment(field, values));
            }
        }

        return string.Join('\n', output);
    }

    public byte[] BuildBytes(IReadOnlyDictionary<string, string> values) =>
        Encoding.UTF8.GetBytes(BuildText(values));

    private static string BuildAssignment(DaqConfigFieldDefinition definition, IReadOnlyDictionary<string, string> values)
    {
        var value = values.TryGetValue(definition.Key, out var configuredValue)
            ? configuredValue
            : definition.DefaultValue;

        return $"{definition.Key}={value}";
    }

    private static bool TryParseAssignment(string line, out string key, out string value)
    {
        var separatorIndex = line.IndexOf('=', StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            key = string.Empty;
            value = string.Empty;
            return false;
        }

        key = line[..separatorIndex];
        var valueStart = separatorIndex + 1;
        var secondSeparatorIndex = line.IndexOf('=', valueStart);
        value = secondSeparatorIndex < 0
            ? line[valueStart..]
            : line[valueStart..secondSeparatorIndex];
        return true;
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        var result = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            var end = index > 0 && text[index - 1] == '\r' ? index - 1 : index;
            result.Add(text[start..end]);
            start = index + 1;
        }

        if (start < text.Length)
        {
            result.Add(text[start..]);
        }

        return result;
    }

    private sealed record DaqConfigLine(string Text, DaqConfigFieldDefinition? Definition);
}
