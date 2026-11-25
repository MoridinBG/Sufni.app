using Mapsui.Nts;
using Mapsui.Styles;
using Sufni.App.MapsAndTracks.Services;

namespace Sufni.App.Tests.MapsAndTracks.Services;

public class MapGeometryFactoryTests
{
    private static readonly MapGeometryInput Input = new([], [new(100, 0, 0, null), new(104, 40, 20, null)], null,
        [new(0, 1.5, Avalonia.Media.Colors.Blue), new(1.5, 4, Avalonia.Media.Colors.Red)], 100);

    private readonly MapGeometryFactory factory = new(new InlineBackgroundTaskRunner());

    [Fact]
    public async Task TerrainRunsColourSeparateTrackFeaturesThatShareTheirBorder()
    {
        var colored = await factory.BuildAsync(Input, null, TestContext.Current.CancellationToken);
        var parts = colored.SessionTrack.LineFeatures.Cast<GeometryFeature>().ToArray();
        Assert.Equal(2, parts.Length);
        var border = parts[0].Geometry!.Coordinates[^1];
        Assert.Equal((15d, 7.5), (border.X, border.Y));
        Assert.Equal(border, parts[1].Geometry!.Coordinates[0]);
        Assert.NotEqual(Assert.Single(parts[0].Styles.OfType<VectorStyle>()).Line!.Color,
            Assert.Single(parts[1].Styles.OfType<VectorStyle>()).Line!.Color);
    }

    [Fact]
    public async Task GroundWithoutAClassDrawsThinAndDashedBetweenColouredRuns()
    {
        var input = Input with { TerrainRuns = [new(0, 1, Avalonia.Media.Colors.Blue), new(3, 4, Avalonia.Media.Colors.Red)] };
        var built = await factory.BuildAsync(input, null, TestContext.Current.CancellationToken);
        var pens = built.SessionTrack.LineFeatures.Select(f => Assert.Single(f.Styles.OfType<VectorStyle>()).Line!).ToArray();
        Assert.Equal(3, pens.Length);
        Assert.Equal((PenStyle.Dash, 3d), (pens[1].PenStyle, pens[1].Width));
        Assert.Equal(PenStyle.Solid, pens[0].PenStyle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HiddenClassesDrawThinAndSolid(bool unclassifiedHidden)
    {
        var input = Input with { TerrainRuns = [new(0, 1, null), new(3, 4, Avalonia.Media.Colors.Red)], UnclassifiedHidden = unclassifiedHidden };
        var built = await factory.BuildAsync(input, null, TestContext.Current.CancellationToken);
        var pens = built.SessionTrack.LineFeatures.Select(f => Assert.Single(f.Styles.OfType<VectorStyle>()).Line!).ToArray();
        Assert.Equal(3, pens.Length);
        Assert.Equal(PenStyle.Solid, pens[0].PenStyle);
        Assert.True(pens[0].Width < pens[2].Width);
        Assert.Equal(unclassifiedHidden, (pens[1].PenStyle, pens[1].Width) == (pens[0].PenStyle, pens[0].Width));
    }

    [Fact]
    public async Task PlainColouringReplacesTerrainFeaturesWithOneTrackFeature()
    {
        var colored = await factory.BuildAsync(Input, null, TestContext.Current.CancellationToken);
        var plain = await factory.BuildAsync(Input with { TerrainRuns = [] }, colored, TestContext.Current.CancellationToken);
        Assert.Single(plain.SessionTrack.LineFeatures);
    }
}
