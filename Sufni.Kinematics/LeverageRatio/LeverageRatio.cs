using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public sealed class LeverageRatio
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IReadOnlyList<LeverageRatioPoint> points;

    [JsonConstructor]
    public LeverageRatio(IReadOnlyList<LeverageRatioPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var errors = LeverageRatioValidation.Validate(points);
        if (errors.Count > 0)
        {
            throw new LeverageRatioValidationException(errors);
        }

        this.points = [.. points];
    }

    [JsonPropertyName("points")]
    public IReadOnlyList<LeverageRatioPoint> Points => points;

    public double MaxShockStroke => points[^1].ShockTravelMm;

    public double MaxWheelTravel => points[^1].WheelTravelMm;

    public static LeverageRatio FromPoints(IReadOnlyList<LeverageRatioPoint> points)
    {
        return new LeverageRatio([.. points]);
    }

    public static LeverageRatio? FromJson(string json)
    {
        try
        {
            var model = JsonSerializer.Deserialize<LeverageRatioJsonModel>(json, JsonOptions);
            return model?.Points is null ? null : FromPoints(model.Points);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (LeverageRatioValidationException)
        {
            return null;
        }
    }

    public string ToJson() => JsonSerializer.Serialize(new LeverageRatioJsonModel([.. points]), JsonOptions);

    public double WheelTravelAt(double shockStroke)
    {
        if (shockStroke <= points[0].ShockTravelMm)
        {
            return points[0].WheelTravelMm;
        }

        if (shockStroke >= MaxShockStroke)
        {
            return MaxWheelTravel;
        }

        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            if (shockStroke > current.ShockTravelMm)
            {
                continue;
            }

            var segmentProgress = (shockStroke - previous.ShockTravelMm) / (current.ShockTravelMm - previous.ShockTravelMm);
            return previous.WheelTravelMm + (current.WheelTravelMm - previous.WheelTravelMm) * segmentProgress;
        }

        return MaxWheelTravel;
    }

    public IReadOnlyList<LeverageRatioSample> DeriveLeverageRatioSamples()
    {
        return points
            .Zip(points.Skip(1), (previous, current) => new LeverageRatioSample(
                WheelTravelMm: (previous.WheelTravelMm + current.WheelTravelMm) / 2.0,
                Ratio: (current.WheelTravelMm - previous.WheelTravelMm) / (current.ShockTravelMm - previous.ShockTravelMm)))
            .ToArray();
    }

    private sealed record LeverageRatioJsonModel([property: JsonPropertyName("points")] List<LeverageRatioPoint> Points);
}