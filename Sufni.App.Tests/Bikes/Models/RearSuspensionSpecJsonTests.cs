using System.Text.Json;
using System.Text.Json.Serialization;
using Sufni.App.Bikes.Models;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Bikes.Models;

public class RearSuspensionSpecJsonTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) }
    };

    public static IEnumerable<object[]> RoundTripCases()
    {
        yield return [new RearSuspensionSpec.Hardtail(), RearSuspensionKind.None];
        yield return [new RearSuspensionSpec.LinkageDraft(), RearSuspensionKind.Linkage];
        yield return [new RearSuspensionSpec.LeverageRatioDraft(), RearSuspensionKind.LeverageRatio];
        yield return [new RearSuspensionSpec.Linkage(SampleLinkage()), RearSuspensionKind.Linkage];
        yield return [new RearSuspensionSpec.LeverageRatio(SampleLeverageRatio()), RearSuspensionKind.LeverageRatio];
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrip_PreservesUnionCaseAndKindProjection(
        RearSuspensionSpec spec,
        RearSuspensionKind expectedKind)
    {
        var json = JsonSerializer.Serialize(spec, Options);

        var parsed = JsonSerializer.Deserialize<RearSuspensionSpec>(json, Options);

        Assert.Equal(spec, parsed);
        Assert.Equal(expectedKind, parsed!.Kind);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "kind": "unknown" }""")]
    [InlineData("""{ "kind": "linkage" }""")]
    [InlineData("""{ "kind": "leverage_ratio" }""")]
    [InlineData("""{ "kind": "hardtail", "linkage": {} }""")]
    [InlineData("""{ "kind": "linkage_draft", "leverage_ratio": {} }""")]
    [InlineData("""{ "kind": "leverage_ratio_draft", "linkage": {} }""")]
    [InlineData("""{ "kind": "linkage", "linkage": {}, "leverage_ratio": {} }""")]
    [InlineData("""{ "kind": "hardtail", "unexpected": true }""")]
    [InlineData("""{ "kind": "hardtail", "kind": "linkage_draft" }""")]
    [InlineData("""{ "kind": "hardtail", "linkage": null, "linkage": null }""")]
    public void Deserialize_RejectsMalformedUnion(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RearSuspensionSpec>(json, Options));
    }

    private static LinkageSpec SampleLinkage() => new(
        [
            new JointSpec("A", JointType.Fixed, 0, 0),
            new JointSpec("B", JointType.Floating, 3, 4)
        ],
        [],
        new LinkSpec("A", "B"),
        10);

    private static LeverageRatioSpec SampleLeverageRatio() =>
        LeverageRatioSpec.FromPoints(
        [
            new LeverageRatioPoint(0, 0),
            new LeverageRatioPoint(10, 25)
        ]);
}
