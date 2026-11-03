using System.Text.Json;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Tests.TestSupport.Fixtures;
using Xunit;

namespace Sufni.App.Tests.Setups.Models.SensorConfigurations;

// Persisted sensor payloads retain their discriminator and authored fields.
public class SensorConfigurationJsonTests
{
    // Independent persisted payloads for the supported sensor variants.
    private const string LinearForkExpectedJson = """{"length":100,"resolution":12,"type":"linear_fork"}""";
    private const string RotationalForkExpectedJson = """{"max_length":205,"arm_length":60,"type":"rotational_fork"}""";
    private const string LinearShockExpectedJson = """{"length":55,"resolution":10,"type":"linear_shock"}""";
    private const string LinearShockStrokeExpectedJson = """{"length":55,"resolution":10,"type":"linear_shock_stroke"}""";
    private const string RotationalShockExpectedJson =
        """{"central_joint":"Bottom bracket","adjacent_joint_1":"Rear wheel","adjacent_joint_2":"Shock eye 1","type":"rotational_shock"}""";

    [Theory]
    [InlineData(LinearForkExpectedJson)]
    [InlineData(RotationalForkExpectedJson)]
    [InlineData(LinearShockExpectedJson)]
    [InlineData(LinearShockStrokeExpectedJson)]
    [InlineData(RotationalShockExpectedJson)]
    public void FromJson_ThenToJson_PreservesSchemaAndCalibration(string json)
    {
        var configuration = SensorConfiguration.FromJson(json);

        Assert.NotNull(configuration);
        using var expected = JsonDocument.Parse(json);
        using var actual = JsonDocument.Parse(SensorConfiguration.ToJson(configuration));
        Assert.Equal(expected.RootElement.EnumerateObject().Count(), actual.RootElement.EnumerateObject().Count());
        foreach (var property in expected.RootElement.EnumerateObject())
        {
            var value = actual.RootElement.GetProperty(property.Name);
            Assert.Equal(property.Value.ValueKind, value.ValueKind);
            if (value.ValueKind == JsonValueKind.Number)
                Assert.Equal(property.Value.GetDouble(), value.GetDouble());
            else
                Assert.Equal(property.Value.GetString(), value.GetString());
        }
    }

    [Fact]
    public void FromJson_MapsLinearShockVariants_ToSharedConcreteType_PreservingDiscriminator()
    {
        var linearShock = SensorConfiguration.FromJson(LinearShockExpectedJson);
        var linearShockStroke = SensorConfiguration.FromJson(LinearShockStrokeExpectedJson);

        Assert.IsType<LinearShockSensorConfiguration>(linearShock);
        Assert.IsType<LinearShockSensorConfiguration>(linearShockStroke);
        Assert.Equal(SensorType.LinearShock, linearShock.Type);
        Assert.Equal(SensorType.LinearShockStroke, linearShockStroke.Type);
    }

    [Fact]
    public void FromJson_ResolvesDiscriminator_CaseInsensitively()
    {
        var json = """{"length":100,"resolution":12,"TYPE":"linear_fork"}""";

        var configuration = SensorConfiguration.FromJson(json);

        var linearFork = Assert.IsType<LinearForkSensorConfiguration>(configuration);
        Assert.Equal(100, linearFork.Length);
        Assert.Equal(SensorType.LinearFork, linearFork.Type);
    }

    [Fact]
    public void FromJson_ReturnsNull_ForObjectWithoutDiscriminator()
    {
        Assert.Null(SensorConfiguration.FromJson("{}"));
    }

    [Fact]
    public void FromJson_ReturnsNull_ForJsonNull()
    {
        Assert.Null(SensorConfiguration.FromJson("null"));
    }

    [Fact]
    public void FromJsonWithBike_BindsLinearForkCalibration()
    {
        var bike = TestSnapshots.Bike() with { HeadAngle = 30, ForkStroke = 120 };

        var configuration = SensorConfiguration.FromJson(LinearForkExpectedJson, bike);

        var linearFork = Assert.IsType<LinearForkSensorConfiguration>(configuration);
        Assert.Equal(60, linearFork.MaxTravel, precision: 6);
        Assert.Equal(50, linearFork.MeasurementToTravel(4095), precision: 6);
    }

    [Fact]
    public void FromJsonWithBike_BindsRotationalForkCalibration()
    {
        var bike = TestSnapshots.Bike() with { HeadAngle = 30, ForkStroke = 120 };
        var json = """{"max_length":100,"arm_length":60,"type":"rotational_fork"}""";

        var configuration = SensorConfiguration.FromJson(json, bike);

        var rotationalFork = Assert.IsType<RotationalForkSensorConfiguration>(configuration);
        Assert.Equal(60, rotationalFork.MaxTravel, precision: 6);
        Assert.Equal(0, rotationalFork.MeasurementToTravel(0), precision: 6);
    }

    [Theory]
    [InlineData(LinearShockExpectedJson)]
    [InlineData(RotationalShockExpectedJson)]
    public void FromJsonWithBike_ReturnsNull_ForRearSensorPayloads(string json)
    {
        var bike = TestSnapshots.Bike();

        Assert.Null(SensorConfiguration.FromJson(json, bike));
    }

    [Fact]
    public void FromJsonWithBike_ReturnsNull_ForUnknownNumericType()
    {
        var bike = TestSnapshots.Bike();

        Assert.Null(SensorConfiguration.FromJson("""{"type":999}""", bike));
    }

    [Fact]
    public void FromJsonWithBike_Throws_ForMalformedJson()
    {
        var bike = TestSnapshots.Bike();

        Assert.Throws<JsonException>(() => SensorConfiguration.FromJson("{", bike));
    }
}
