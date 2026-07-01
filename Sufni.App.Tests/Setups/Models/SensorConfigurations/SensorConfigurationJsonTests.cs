using Sufni.App.Setups.Models.SensorConfigurations;
using Xunit;

namespace Sufni.App.Tests.Setups.Models.SensorConfigurations;

// Guards the persisted/sync wire-format contract for sensor configurations: the polymorphic
// SensorConfigurationJsonConverter must keep byte-identical output for every SensorType and must
// preserve the load-bearing "type" discriminator (LinearShock vs LinearShockStroke share one CLR
// type). Golden strings lock the exact compact JSON so a format regression fails loudly.
public class SensorConfigurationJsonTests
{
    // Golden compact JSON per SensorType (derived properties first, base "type" last).
    private const string LinearForkGolden = """{"length":100,"resolution":12,"type":"linear_fork"}""";
    private const string RotationalForkGolden = """{"max_length":205,"arm_length":60,"type":"rotational_fork"}""";
    private const string LinearShockGolden = """{"length":55,"resolution":10,"type":"linear_shock"}""";
    private const string LinearShockStrokeGolden = """{"length":55,"resolution":10,"type":"linear_shock_stroke"}""";
    private const string RotationalShockGolden =
        """{"central_joint":"Bottom bracket","adjacent_joint_1":"Rear wheel","adjacent_joint_2":"Shock eye 1","type":"rotational_shock"}""";

    [Fact]
    public void ToJson_LinearFork_MatchesGoldenString()
    {
        var configuration = new LinearForkSensorConfiguration { Length = 100, Resolution = 12 };
        Assert.Equal(LinearForkGolden, SensorConfiguration.ToJson(configuration));
    }

    [Fact]
    public void ToJson_RotationalFork_MatchesGoldenString()
    {
        var configuration = new RotationalForkSensorConfiguration { MaxLength = 205, ArmLength = 60 };
        Assert.Equal(RotationalForkGolden, SensorConfiguration.ToJson(configuration));
    }

    [Fact]
    public void ToJson_LinearShock_MatchesGoldenString()
    {
        var configuration = new LinearShockSensorConfiguration { Length = 55, Resolution = 10 };
        Assert.Equal(LinearShockGolden, SensorConfiguration.ToJson(configuration));
    }

    [Fact]
    public void ToJson_LinearShockStroke_MatchesGoldenString()
    {
        // Same CLR type as LinearShock; the overridden Type must round-trip into the discriminator.
        var configuration = new LinearShockSensorConfiguration
        {
            Length = 55,
            Resolution = 10,
            Type = SensorType.LinearShockStroke,
        };
        Assert.Equal(LinearShockStrokeGolden, SensorConfiguration.ToJson(configuration));
    }

    [Fact]
    public void ToJson_RotationalShock_MatchesGoldenString()
    {
        var configuration = new RotationalShockSensorConfiguration
        {
            CentralJoint = "Bottom bracket",
            AdjacentJoint1 = "Rear wheel",
            AdjacentJoint2 = "Shock eye 1",
        };
        Assert.Equal(RotationalShockGolden, SensorConfiguration.ToJson(configuration));
    }

    [Theory]
    [InlineData(LinearForkGolden)]
    [InlineData(RotationalForkGolden)]
    [InlineData(LinearShockGolden)]
    [InlineData(LinearShockStrokeGolden)]
    [InlineData(RotationalShockGolden)]
    public void FromJson_ThenToJson_PreservesBytes(string json)
    {
        // Single-parse polymorphic deserialize followed by serialize must reproduce the input exactly.
        var configuration = SensorConfiguration.FromJson(json);

        Assert.NotNull(configuration);
        Assert.Equal(json, SensorConfiguration.ToJson(configuration));
    }

    [Fact]
    public void FromJson_MapsLinearShockVariants_ToSharedConcreteType_PreservingDiscriminator()
    {
        var linearShock = SensorConfiguration.FromJson(LinearShockGolden);
        var linearShockStroke = SensorConfiguration.FromJson(LinearShockStrokeGolden);

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
}
