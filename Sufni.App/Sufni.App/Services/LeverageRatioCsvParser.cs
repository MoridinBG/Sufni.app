using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Sufni.Kinematics;

namespace Sufni.App.Services;

public static class LeverageRatioCsvParser
{
    private const string ShockTravelHeader = "shock_travel_mm";
    private const string WheelTravelHeader = "wheel_travel_mm";

    public static LeverageRatioParseResult Parse(Stream csvStream)
    {
        ArgumentNullException.ThrowIfNull(csvStream);

        using var reader = new StreamReader(csvStream, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    public static LeverageRatioParseResult Parse(string csvText)
    {
        ArgumentNullException.ThrowIfNull(csvText);

        var normalized = csvText.TrimStart('\uFEFF');
        var lines = ReadNonEmptyLines(normalized);
        if (lines.Count == 0)
        {
            return new LeverageRatioParseResult.Invalid([new LeverageRatioParseError(null, "CSV is empty.")]);
        }

        List<LeverageRatioParseError> errors = [];
        var (headerLineNumber, headerLine) = lines[0];
        var delimiter = headerLine.Contains(';') ? ';' : ',';

        if (!HasValidHeader(headerLine, delimiter))
        {
            errors.Add(new LeverageRatioParseError(headerLineNumber, "Required CSV header is shock_travel_mm,wheel_travel_mm."));
        }

        List<LeverageRatioPoint> points = [];
        List<int> pointLineNumbers = [];

        foreach (var (lineNumber, line) in lines.Skip(1))
        {
            var columns = line.Split(delimiter);
            if (columns.Length != 2)
            {
                errors.Add(new LeverageRatioParseError(lineNumber, "Each data row must contain exactly two columns."));
                continue;
            }

            var shockText = columns[0].Trim();
            var wheelText = columns[1].Trim();
            if (shockText.Contains(',') || wheelText.Contains(','))
            {
                errors.Add(new LeverageRatioParseError(lineNumber, "Decimal comma is not supported; use '.' as the decimal separator."));
                continue;
            }

            if (!double.TryParse(shockText, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var shockTravel))
            {
                errors.Add(new LeverageRatioParseError(lineNumber, "Shock travel must be a valid number."));
                continue;
            }

            if (!double.TryParse(wheelText, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var wheelTravel))
            {
                errors.Add(new LeverageRatioParseError(lineNumber, "Wheel travel must be a valid number."));
                continue;
            }

            points.Add(new LeverageRatioPoint(shockTravel, wheelTravel));
            pointLineNumbers.Add(lineNumber);
        }

        foreach (var error in LeverageRatioValidation.Validate(points))
        {
            int? lineNumber = error.PointIndex is int pointIndex && pointIndex >= 0 && pointIndex < pointLineNumbers.Count
                ? pointLineNumbers[pointIndex]
                : null;
            errors.Add(new LeverageRatioParseError(lineNumber, error.Message));
        }

        if (errors.Count > 0)
        {
            return new LeverageRatioParseResult.Invalid(errors);
        }

        return new LeverageRatioParseResult.Parsed(LeverageRatio.FromPoints(points));
    }

    private static List<(int LineNumber, string Line)> ReadNonEmptyLines(string csvText)
    {
        List<(int LineNumber, string Line)> lines = [];
        using var reader = new StringReader(csvText);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            lines.Add((lineNumber, line.Trim()));
        }

        return lines;
    }

    private static bool HasValidHeader(string headerLine, char delimiter)
    {
        var columns = headerLine.Split(delimiter)
            .Select(column => column.Trim())
            .ToArray();
        return columns.Length == 2 &&
               string.Equals(columns[0], ShockTravelHeader, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(columns[1], WheelTravelHeader, StringComparison.OrdinalIgnoreCase);
    }
}

public abstract record LeverageRatioParseResult
{
    private LeverageRatioParseResult() { }

    public sealed record Parsed(LeverageRatio Value) : LeverageRatioParseResult;

    public sealed record Invalid(IReadOnlyList<LeverageRatioParseError> Errors) : LeverageRatioParseResult;
}

public sealed record LeverageRatioParseError(int? LineNumber, string Message);