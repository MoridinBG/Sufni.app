using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public sealed class LeverageRatioSpec : IEquatable<LeverageRatioSpec>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LeverageRatioPoint[] points;
    private readonly ReadOnlyCollection<LeverageRatioPoint> pointView;
    private readonly CoordinateList travelCurve;

    [JsonConstructor]
    public LeverageRatioSpec(IReadOnlyList<LeverageRatioPoint> points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var errors = LeverageRatioValidation.Validate(points);
        if (errors.Count > 0)
        {
            throw new LeverageRatioValidationException(errors);
        }

        this.points = [.. points];
        pointView = Array.AsReadOnly(this.points);
        travelCurve = new CoordinateList(
            this.points.Select(point => point.ShockTravelMm).ToList(),
            this.points.Select(point => point.WheelTravelMm).ToList());
    }

    [JsonPropertyName("points")]
    public IReadOnlyList<LeverageRatioPoint> Points => pointView;

    public double MaxShockStroke => travelCurve.X[^1];

    public double MaxWheelTravel => travelCurve.Y[^1];

    public static LeverageRatioSpec FromPoints(IReadOnlyList<LeverageRatioPoint> points)
    {
        return new LeverageRatioSpec([.. points]);
    }

    public static LeverageRatioSpec? FromJson(string json)
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

    public bool Equals(LeverageRatioSpec? other)
    {
        if (ReferenceEquals(this, other))
        {
            return true;
        }

        if (other is null || points.Length != other.points.Length)
        {
            return false;
        }

        for (var index = 0; index < points.Length; index++)
        {
            if (!PointEquals(points[index], other.points[index]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is LeverageRatioSpec other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var point in points)
        {
            hash.Add(DoubleBits(point.ShockTravelMm));
            hash.Add(DoubleBits(point.WheelTravelMm));
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(LeverageRatioSpec? left, LeverageRatioSpec? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(LeverageRatioSpec? left, LeverageRatioSpec? right) => !(left == right);

    private static bool PointEquals(LeverageRatioPoint left, LeverageRatioPoint right) =>
        DoubleBits(left.ShockTravelMm) == DoubleBits(right.ShockTravelMm) &&
        DoubleBits(left.WheelTravelMm) == DoubleBits(right.WheelTravelMm);

    private static long DoubleBits(double value) => BitConverter.DoubleToInt64Bits(value);

    private sealed record LeverageRatioJsonModel([property: JsonPropertyName("points")] List<LeverageRatioPoint> Points);
}
