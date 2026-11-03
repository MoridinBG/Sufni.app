namespace Sufni.Telemetry.Tests;

public class TelemetryCodecSchemaTests
{
    [Theory]
    [InlineData("processed-imu-dense-only")]
    [InlineData("processed-imu-segment-only")]
    [InlineData("processed-imu-dense-plus-segment")]
    [InlineData("processed-old-stroke-digitization")]
    [InlineData("processed-source-less")]
    public async Task LegacyGraph_RewritesToFrozenCurrentBytes(string id)
    {
        var source = GoldenTelemetryCompatibilityFixtures.All.Single(fixture => fixture.Id == id);
        var expected = await ReadFixtureAsync(id);

        Assert.Equal(expected, TelemetryData.FromBinary(source.GetBytes()).BinaryForm);
    }

    [Fact]
    public async Task FullGraph_UsesFrozenCurrentSchema()
    {
        var expected = await ReadFixtureAsync("full-graph");

        Assert.Equal(expected, TelemetryCodecFixtureData.FullGraph().BinaryForm);
        Assert.Equal(expected, TelemetryData.FromBinary(expected).BinaryForm);
    }

    [Fact]
    public async Task LiveSource_UsesFrozenJsonAndPreservesCapturedDataOnRead()
    {
        var expected = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "Fixtures", "Codec", "live-capture-v1.json"),
            TestContext.Current.CancellationToken);
        var capture = TelemetryCodecFixtureData.LiveCapture();
        var payload = TelemetryCodec.DecodeLiveSource(expected);

        Assert.Equal(expected, TelemetryCodec.EncodeLiveSource(RecordedLiveCaptureSourcePayload.FromCapture(capture)));
        Assert.Equal(expected, TelemetryCodec.EncodeLiveSource(payload));
        Assert.Equal(TelemetryData.FromLiveCapture(capture).BinaryForm,
            TelemetryData.FromLiveCapture(payload.ToCapture(capture.BikeData)).BinaryForm);
    }

    private static Task<byte[]> ReadFixtureAsync(string name) =>
        File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Codec", name + ".msgpack"),
            TestContext.Current.CancellationToken);
}
