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
    private readonly CoordinateList travelCurve;

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
        travelCurve = new CoordinateList(
            this.points.Select(point => point.ShockTravelMm).ToList(),
            this.points.Select(point => point.WheelTravelMm).ToList());
    }

    [JsonPropertyName("points")]
    public IReadOnlyList<LeverageRatioPoint> Points => points;

    public double MaxShockStroke => travelCurve.X[^1];

    public double MaxWheelTravel => travelCurve.Y[^1];

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

    public double WheelTravelAt(double shockStroke) => TravelInterpolation.WheelTravelAt(travelCurve, shockStroke);

    public CoordinateList DeriveLeverageRatioData() => LeverageRatioDerivation.DeriveData(travelCurve.X, travelCurve.Y);

    public IReadOnlyList<LeverageRatioSample> DeriveLeverageRatioSamples() =>
        LeverageRatioDerivation.DeriveSamples(travelCurve.X, travelCurve.Y);

    private sealed record LeverageRatioJsonModel([property: JsonPropertyName("points")] List<LeverageRatioPoint> Points);
}
