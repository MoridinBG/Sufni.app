namespace Sufni.Telemetry.Tests;

public class SignalFiltersTests
{
    [Theory]
    [InlineData(false, 1, 0.98, 1.02)]
    [InlineData(false, 10, 0.49, 0.51)]
    [InlineData(false, 100, 0, 0.1)]
    [InlineData(true, 0.5, 0, 0.1)]
    [InlineData(true, 5, 0.49, 0.51)]
    [InlineData(true, 10, 0.98, 1.02)]
    [InlineData(true, 20, 0.49, 0.51)]
    [InlineData(true, 200, 0, 0.1)]
    public void ToneAmplitudeFollowsTheSelectedBand(bool bandPass, double frequency, double lower, double upper)
    {
        const int rate = 500;
        var tone = Enumerable.Range(0, 10 * rate).Select(i => Math.Sin(2 * Math.PI * frequency * i / rate)).ToArray();
        var filtered = bandPass ? SignalFilters.BandPass(tone, rate, 5, 20) : SignalFilters.LowPass(tone, rate, 10);
        var rms = Math.Sqrt(filtered.Skip(2 * rate).Take(6 * rate).Average(v => v * v));
        Assert.InRange(rms * Math.Sqrt(2), lower, upper);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IsolatedPulseKeepsItsPeakTime(bool bandPass)
    {
        var pulse = new double[1000];
        pulse[410] = 1;
        var filtered = bandPass ? SignalFilters.BandPass(pulse, 200, 5, 20) : SignalFilters.LowPass(pulse, 200, 10);
        Assert.Equal(410, Array.IndexOf(filtered, filtered.Max()));
    }
}
