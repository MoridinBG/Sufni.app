using System.Text.Json;
using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class LinkageSpecTests
{
    [Fact]
    public void ToJson_SerializesCurrentLinkageJsonShape()
    {
        var spec = SampleSpec();

        using var document = JsonDocument.Parse(spec.ToJson());
        var root = document.RootElement;

        Assert.True(root.TryGetProperty("joints", out var joints));
        Assert.True(root.TryGetProperty("links", out var links));
        Assert.True(root.TryGetProperty("shock", out var shock));
        Assert.True(root.TryGetProperty("shock_stroke", out var shockStroke));
        Assert.Equal("bottom_bracket", joints[0].GetProperty("type").GetString());
        Assert.Equal("Bottom bracket", joints[0].GetProperty("name").GetString());
        Assert.Equal("Rear wheel", links[0].GetProperty("b").GetString());
        Assert.Equal("Shock eye 1", shock.GetProperty("a").GetString());
        Assert.Equal(55, shockStroke.GetDouble());
    }

    [Fact]
    public void FromJson_DeserializesWithoutResolvingOrNormalizingNullJointTypes()
    {
        const string json = """
        {
          "joints": [
            { "name": "A", "x": 0, "y": 0 },
            { "name": "B", "type": null, "x": 3, "y": 4 }
          ],
          "links": [
            { "a": "A", "b": "B" }
          ],
          "shock": { "a": "A", "b": "B" },
          "shock_stroke": 10
        }
        """;

        var spec = LinkageSpec.FromJson(json);
        var roundTripped = LinkageSpec.FromJson(spec.ToJson());

        Assert.Null(spec.Joints[0].Type);
        Assert.Null(spec.Joints[1].Type);
        Assert.Equal(spec, roundTripped);
    }

    [Fact]
    public void Constructor_CopiesIncomingLists()
    {
        List<JointSpec> joints =
        [
            new("A", JointType.Fixed, 0, 0),
            new("B", JointType.Floating, 3, 4)
        ];
        List<LinkSpec> links = [new("A", "B")];

        var spec = new LinkageSpec(joints, links, new LinkSpec("A", "B"), 10);
        joints[0] = new JointSpec("Changed", JointType.Fixed, 0, 0);
        links[0] = new LinkSpec("Changed", "B");

        Assert.Equal("A", spec.Joints[0].Name);
        Assert.Equal("A", spec.Links[0].A);
    }

    [Fact]
    public void WithShockStroke_ReturnsNewValue()
    {
        var spec = SampleSpec();

        var updated = spec.WithShockStroke(60);

        Assert.Equal(55, spec.ShockStroke);
        Assert.Equal(60, updated.ShockStroke);
        Assert.Equal(spec.Joints, updated.Joints);
        Assert.Equal(spec.Links, updated.Links);
        Assert.Equal(spec.Shock, updated.Shock);
    }

    [Fact]
    public void StructuralEquality_UsesOrderAndExactDoubleBits()
    {
        var spec = new LinkageSpec(
            [new JointSpec("A", JointType.Fixed, 0.0, 0), new JointSpec("B", JointType.Floating, 1, 1)],
            [new LinkSpec("A", "B")],
            new LinkSpec("A", "B"),
            10);
        var same = new LinkageSpec(
            [new JointSpec("A", JointType.Fixed, 0.0, 0), new JointSpec("B", JointType.Floating, 1, 1)],
            [new LinkSpec("A", "B")],
            new LinkSpec("A", "B"),
            10);
        var negativeZero = new LinkageSpec(
            [new JointSpec("A", JointType.Fixed, -0.0, 0), new JointSpec("B", JointType.Floating, 1, 1)],
            [new LinkSpec("A", "B")],
            new LinkSpec("A", "B"),
            10);
        var reordered = new LinkageSpec(
            [new JointSpec("B", JointType.Floating, 1, 1), new JointSpec("A", JointType.Fixed, 0.0, 0)],
            [new LinkSpec("A", "B")],
            new LinkSpec("A", "B"),
            10);

        Assert.Equal(spec, same);
        Assert.Equal(spec.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(spec, negativeZero);
        Assert.NotEqual(spec, reordered);
    }

    private static LinkageSpec SampleSpec() => new(
        [
            new JointSpec("Bottom bracket", JointType.BottomBracket, 0, 0),
            new JointSpec("Rear wheel", JointType.RearWheel, 450, 10),
            new JointSpec("Shock eye 1", JointType.Floating, 180, 220),
            new JointSpec("Shock eye 2", JointType.Fixed, 120, 280)
        ],
        [
            new LinkSpec("Bottom bracket", "Rear wheel"),
            new LinkSpec("Rear wheel", "Shock eye 1")
        ],
        new LinkSpec("Shock eye 1", "Shock eye 2"),
        55);
}
