using Avalonia.Media;
using Sufni.App.Infrastructure.Theming;
using Sufni.App.Terrain.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.Plots;

public class TerrainColorsTests
{
    private static readonly SufniTerrainScales Scales = SufniThemes.SignalSeries.Terrain;

    [Theory]
    [InlineData(-1.5, RoughnessClass.Smooth)]
    [InlineData(0.5, RoughnessClass.Medium)]
    [InlineData(2.5, RoughnessClass.VeryRough)]
    [InlineData(-4, RoughnessClass.Smooth)]
    [InlineData(6, RoughnessClass.VeryRough)]
    public void RoughnessShadeIsTheClassColourAtItsCentreAndTheEndColourOutside(double value, RoughnessClass colour)
    {
        Assert.Equal(Scales.ColorFor(colour), Scales.ShadeFor(RoughnessClass.Medium, value));
    }

    [Theory]
    [InlineData(-8, GradeBand.SteepClimb)]
    [InlineData(3.5, GradeBand.GentleDescent)]
    [InlineData(17.5, GradeBand.VerySteepDescent)]
    [InlineData(-20, GradeBand.SteepClimb)]
    [InlineData(30, GradeBand.VerySteepDescent)]
    public void GradeShadeIsTheBandColourAtItsPositionAndTheEndColourOutside(double degrees, GradeBand colour)
    {
        Assert.Equal(Scales.ColorFor(colour), Scales.ShadeFor(GradeBand.Flat, degrees));
    }

    [Fact]
    public void ShadeBetweenTwoClassCentresBlendsEachChannel()
    {
        var (calm, medium) = (Scales.ColorFor(RoughnessClass.Calm), Scales.ColorFor(RoughnessClass.Medium));
        Assert.Equal(Color.FromArgb(Middle(calm.A, medium.A), Middle(calm.R, medium.R), Middle(calm.G, medium.G),
            Middle(calm.B, medium.B)), Scales.ShadeFor(RoughnessClass.Calm, 0));
    }

    [Fact]
    public void ShadeWithoutValueIsThePlainClassColour()
    {
        Assert.Equal(Scales.ColorFor(RoughnessClass.TechnicalClimb), Scales.ShadeFor(RoughnessClass.TechnicalClimb, null));
        Assert.Equal(Scales.ColorFor(GradeBand.Descent), Scales.ShadeFor(GradeBand.Descent, null));
    }

    private static byte Middle(byte a, byte b) => (byte)Math.Round((a + b) / 2.0);
}
