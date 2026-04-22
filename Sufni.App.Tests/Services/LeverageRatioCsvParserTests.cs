using System.Text;
using Sufni.App.Services;

namespace Sufni.App.Tests.Services;

public class LeverageRatioCsvParserTests
{
    [Fact]
    public void Parse_CommaDelimitedCsv_ReturnsParsedCurve()
    {
        const string csv = "shock_travel_mm,wheel_travel_mm\n0,0\n10,25\n20,50\n";

        var result = LeverageRatioCsvParser.Parse(csv);

        var parsed = Assert.IsType<LeverageRatioParseResult.Parsed>(result);
        Assert.Equal(50, parsed.Value.MaxWheelTravel);
        Assert.Equal(25, parsed.Value.WheelTravelAt(10));
    }

    [Fact]
    public void Parse_SemicolonDelimitedCsvFromStream_ReturnsParsedCurve()
    {
        const string csv = "shock_travel_mm;wheel_travel_mm\n0;0\n10;25\n20;50\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray());

        var result = LeverageRatioCsvParser.Parse(stream);

        var parsed = Assert.IsType<LeverageRatioParseResult.Parsed>(result);
        Assert.Equal(20, parsed.Value.MaxShockStroke);
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenHeaderDoesNotMatchContract()
    {
        const string csv = "shock,wheel\n0,0\n10,25\n";

        var result = LeverageRatioCsvParser.Parse(csv);

        var invalid = Assert.IsType<LeverageRatioParseResult.Invalid>(result);
        Assert.Contains(invalid.Errors, error => error.Message.Contains("Required CSV header", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_ReturnsInvalid_WhenDecimalCommaIsUsed()
    {
        const string csv = "shock_travel_mm;wheel_travel_mm\n0,5;10,2\n10;20\n";

        var result = LeverageRatioCsvParser.Parse(csv);

        var invalid = Assert.IsType<LeverageRatioParseResult.Invalid>(result);
        Assert.Contains(invalid.Errors, error => error.Message.Contains("Decimal comma", StringComparison.Ordinal));
    }

    [Fact]
    public void Parse_CollectsRowAndCurveValidationErrors()
    {
        const string csv = "shock_travel_mm,wheel_travel_mm\n0,0\nfoo,10\n5,20\n4,30\n";

        var result = LeverageRatioCsvParser.Parse(csv);

        var invalid = Assert.IsType<LeverageRatioParseResult.Invalid>(result);
        Assert.Contains(invalid.Errors, error => error.Message.Contains("Shock travel must be a valid number.", StringComparison.Ordinal));
        Assert.Contains(invalid.Errors, error => error.Message.Contains("Shock travel must increase monotonically.", StringComparison.Ordinal));
    }
}