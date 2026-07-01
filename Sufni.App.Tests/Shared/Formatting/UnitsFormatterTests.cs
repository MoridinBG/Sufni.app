using System.Globalization;

using Sufni.App.Shared.Formatting;
namespace Sufni.App.Tests.Shared.Formatting;

public class UnitsFormatterTests
{
    [Fact]
    public void FormatNumber_HonorsExplicitProvider()
    {
        Assert.Equal("1.2", UnitsFormatter.FormatNumber(1.23, 1, CultureInfo.InvariantCulture));
        Assert.Equal("1,2", UnitsFormatter.FormatNumber(1.23, 1, CultureInfo.GetCultureInfo("de-DE")));
    }

    [Fact]
    public void FormatDistance_HonorsExplicitProvider()
    {
        Assert.Equal("1.2 km", UnitsFormatter.FormatDistance(1234, CultureInfo.InvariantCulture));
        Assert.Equal("1,2 km", UnitsFormatter.FormatDistance(1234, CultureInfo.GetCultureInfo("de-DE")));
        Assert.Equal("999 m", UnitsFormatter.FormatDistance(999.4, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FormatDuration_FormatsHourMinuteSecondTiers()
    {
        Assert.Equal("1h 01m", UnitsFormatter.FormatDuration(TimeSpan.FromSeconds(3660), CultureInfo.InvariantCulture));
        Assert.Equal("2m 05s", UnitsFormatter.FormatDuration(TimeSpan.FromSeconds(125), CultureInfo.InvariantCulture));
        Assert.Equal("45s", UnitsFormatter.FormatDuration(TimeSpan.FromSeconds(45), CultureInfo.InvariantCulture));
    }

    [Fact]
    public void DisplayDefault_UsesCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("1,2 km", UnitsFormatter.FormatDistance(1234));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }
}
