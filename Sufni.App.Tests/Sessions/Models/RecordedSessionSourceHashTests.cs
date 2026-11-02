using System.Text;
using Sufni.Telemetry;
using Sufni.App.Acquisition.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using ZstdSharp;

namespace Sufni.App.Tests.Sessions.Models;

public class RecordedSessionSourceHashTests
{
    [Fact]
    public void ImportedIdentity_UsesUncompressedBytes_RegardlessOfFileNameOrCompression()
    {
        byte[] bytes = [1, 2, 3];
        using var firstFile = new TelemetryFileSource("first.SST", bytes);
        using var renamedFile = new TelemetryFileSource("renamed.SST", bytes);
        var first = RecordedSessionSourceFactory.CreateImportedSst(firstFile);
        var renamed = RecordedSessionSourceFactory.CreateImportedSst(renamedFile);
        using var compressor = new Compressor(15);
        var otherCompression = compressor.Wrap(bytes).ToArray();

        Assert.Equal("039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81", first.SourceHash);
        Assert.Equal(first.SourceHash, renamed.SourceHash);
        Assert.Equal(first.SessionId, renamed.SessionId);
        Assert.Equal(new Guid("45d276c1-1857-5e7b-99a6-150e48e976bf"), first.SessionId);
        Assert.Equal(first.SourceHash, RecordedSessionSourceHash.Compute(RecordedSessionSourceKind.ImportedSst, otherCompression));
        Assert.NotEqual(first.SourceHash, RecordedSessionSourceHash.ComputeImportedSst([1, 2, 4]));
        Assert.True(RecordedSessionSourceHash.Matches(first));
        Assert.True(RecordedSessionSourceHash.Matches(renamed));
    }

    [Fact]
    public void LiveIdentity_ExcludesSetupDisplayName_AndRetainsCaptureContent()
    {
        const string payload = """{"schema_version":1,"metadata":{"SourceName":"Setup A","Version":1,"SampleRate":100,"Timestamp":123,"Duration":1},"front_measurements":[10,20,30],"imu_data":null,"temperature_data":[],"markers":[]}""";
        var original = Hash(payload);
        Assert.Equal(Hash(payload.Replace("Setup A", "Renamed setup")), original);
        Assert.NotEqual(Hash(payload.Replace("10,20,30", "10,20,31")), original);
        Assert.NotEqual(Hash(payload.Replace("\"Timestamp\":123", "\"Timestamp\":124")), original);
        Assert.Equal("fea4ee8bff4190977c746f51f7bf5acb3fd83eb7e4ff896904f247696abcb3cc", original);
    }

    [Fact]
    public void LiveCaptureFactory_AssignsContentId_IndependentOfSetupDisplayName()
    {
        var capture = new LiveTelemetryCapture(
            new Metadata { SourceName = "setup", Version = 4, Timestamp = 123, SampleRate = 100, Duration = 1 },
            new BikeData(100, null, value => value, null),
            [new RawCountSegment { Counts = [10, 20, 30] }], [], null, null, [], [], null, false);
        var original = RecordedSessionSourceFactory.CreateLiveCapture(capture);
        capture = capture with { Metadata = capture.Metadata with { SourceName = "renamed setup" } };
        var renamed = RecordedSessionSourceFactory.CreateLiveCapture(capture);
        var changed = RecordedSessionSourceFactory.CreateLiveCapture(capture with
        {
            FrontSegments = [new RawCountSegment { Counts = [10, 20, 31] }]
        });

        Assert.Equal(original.SessionId, renamed.SessionId);
        Assert.Equal(original.SourceHash, renamed.SourceHash);
        Assert.NotEqual(original.SessionId, changed.SessionId);
        Assert.Equal("setup", original.SourceName);
        Assert.Equal("renamed setup", renamed.SourceName);
    }

    private static string Hash(string payload) => RecordedSessionSourceHash.Compute(
        RecordedSessionSourceKind.LiveCapture, Encoding.UTF8.GetBytes(payload));
}
