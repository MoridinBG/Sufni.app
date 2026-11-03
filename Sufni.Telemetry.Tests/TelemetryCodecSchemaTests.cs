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

    [Fact]
    public void LegacyLiveSource_WithoutSegmentFields_PreservesDenseImuWhenReencoded()
    {
        var payload = TelemetryCodec.DecodeLiveSource("""
            {"schema_version":1,"metadata":{"SourceName":"live","Version":4,"SampleRate":100,"Timestamp":1700000000,"Duration":0.01},
             "imu_data":{"Meta":[{"LocationId":1,"AccelLsbPerG":4096,"GyroLsbPerDps":32.8}],"SampleRate":100,
             "Records":[{"Ax":1,"Ay":2,"Az":3,"Gx":4,"Gy":5,"Gz":6}],"ActiveLocations":[1]}}
            """u8.ToArray());

        var encoded = TelemetryCodec.EncodeLiveSource(payload);
        var reread = TelemetryCodec.DecodeLiveSource(encoded);

        Assert.Empty(reread.ImuData!.Segments);
        Assert.Equal(new ImuRecord(1, 2, 3, 4, 5, 6), Assert.Single(reread.ImuData.Records));
        Assert.Equal((byte)1, reread.ImuData.SampleSegments[0].LocationId);
        Assert.Equal(1, reread.ImuData.SampleSegments.Count);
    }

    private static Task<byte[]> ReadFixtureAsync(string name) =>
        File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Codec", name + ".msgpack"),
            TestContext.Current.CancellationToken);
}
