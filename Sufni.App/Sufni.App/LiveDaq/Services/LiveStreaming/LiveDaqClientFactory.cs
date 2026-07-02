using System;
using Sufni.App.LiveDaq.Stores;

namespace Sufni.App.LiveDaq.Services.LiveStreaming;

internal sealed class LiveDaqClientFactory : ILiveDaqClientFactory
{
    public ILiveDaqClient Create(LiveDaqSnapshot snapshot) =>
        snapshot.ProtocolVersion switch
        {
            LiveProtocolVersion.V2 => new LiveDaqV2Client(),
            LiveProtocolVersion.V3 => new LiveDaqV3Client(snapshot.BoardId),
            _ => throw new InvalidOperationException($"Unsupported LIVE protocol version {snapshot.ProtocolVersion}."),
        };
}
