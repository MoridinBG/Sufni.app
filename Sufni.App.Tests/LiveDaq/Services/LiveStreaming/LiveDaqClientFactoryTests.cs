using System;
using Sufni.App.LiveDaq.Services.LiveStreaming;
using Sufni.App.LiveDaq.Stores;

namespace Sufni.App.Tests.LiveDaq.Services.LiveStreaming;

public class LiveDaqClientFactoryTests
{
    [Fact]
    public void Create_ForV2Snapshot_ReturnsV2Client()
    {
        var factory = new LiveDaqClientFactory();

        var client = factory.Create(CreateSnapshot(LiveProtocolVersion.V2));

        Assert.IsType<LiveDaqV2Client>(client);
    }

    [Fact]
    public void Create_ForV3Snapshot_ReturnsV3Client()
    {
        var factory = new LiveDaqClientFactory();

        var client = factory.Create(CreateSnapshot(LiveProtocolVersion.V3));

        Assert.IsType<LiveDaqV3Client>(client);
    }

    private static LiveDaqSnapshot CreateSnapshot(LiveProtocolVersion protocolVersion) =>
        new(
            IdentityKey: "board-1",
            DisplayName: "board-1",
            BoardId: "board-1",
            Host: "192.168.0.50",
            Port: 1557,
            IsOnline: true,
            SetupName: null,
            BikeName: null,
            ProtocolVersion: protocolVersion);
}
