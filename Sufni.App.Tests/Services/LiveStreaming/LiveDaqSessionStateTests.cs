using Sufni.App.Services.LiveStreaming;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Services.LiveStreaming;

public class LiveDaqSessionStateTests
{
    [Fact]
    public void CreateSnapshot_ProjectsAcceptedSessionAndLatestSensorValues()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 700);

        state.ApplyFrame(new LiveStartAckFrame(
            CreateHeader(LiveFrameType.StartLiveAck, 0),
            new LiveStartAck(LiveStartErrorCode.Ok, 700, LiveSensorMask.Travel | LiveSensorMask.Imu | LiveSensorMask.Gps)));
        state.ApplyFrame(new LiveSessionHeaderFrame(CreateHeader(LiveFrameType.SessionHeader, 1), sessionHeader));
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveFrameType.TravelBatch, 2),
            new LiveBatchHeader(700, 0, 10, 123456789, 2),
            [new LiveTravelRecord(100, 200), new LiveTravelRecord(111, 222)]));
        state.ApplyFrame(new LiveImuBatchFrame(
            CreateHeader(LiveFrameType.ImuBatch, 3),
            new LiveBatchHeader(700, 0, 20, 123456789, 2),
            [
                new ImuRecord(1, 2, 3, 4, 5, 6),
                new ImuRecord(7, 8, 9, 10, 11, 12),
                new ImuRecord(13, 14, 15, 16, 17, 18),
                new ImuRecord(19, 20, 21, 22, 23, 24)
            ]));
        state.ApplyFrame(new LiveGpsBatchFrame(
            CreateHeader(LiveFrameType.GpsBatch, 4),
            new LiveBatchHeader(700, 0, 30, 123456789, 1),
            [new GpsRecord(new DateTime(2026, 1, 2, 3, 4, 6, DateTimeKind.Utc), 48.2, 16.3, 200f, 8f, 90f, 2, 9, 1.5f, 2.5f)]));
        state.ApplyFrame(new LiveSessionStatsFrame(
            CreateHeader(LiveFrameType.SessionStats, 5),
            new LiveSessionStats(700, 5, 4, 1, 3, 2, 0)));

        var snapshot = state.CreateSnapshot(LiveConnectionState.Connected, null);

        Assert.Equal(LiveConnectionState.Connected, snapshot.ConnectionState);
        Assert.Equal((uint)700, snapshot.Session.SessionId);
        Assert.Equal((uint)200, snapshot.Session.AcceptedTravelHz);
        Assert.True(snapshot.Travel.IsActive);
        Assert.True(snapshot.Travel.HasData);
        Assert.Equal((ushort)111, snapshot.Travel.FrontMeasurement);
        Assert.Equal((ushort)222, snapshot.Travel.RearMeasurement);
        Assert.Equal((uint)5, snapshot.Travel.QueueDepth);
        Assert.Equal((uint)3, snapshot.Travel.DroppedBatches);

        Assert.Equal(2, snapshot.Imus.Count);
        Assert.Equal(LiveImuLocation.Frame, snapshot.Imus[0].Location);
        Assert.True(snapshot.Imus[0].HasData);
        Assert.Equal((short)13, snapshot.Imus[0].Ax);
        Assert.Equal(LiveImuLocation.Rear, snapshot.Imus[1].Location);
        Assert.Equal((short)19, snapshot.Imus[1].Ax);
        Assert.Equal((uint)4, snapshot.Imus[0].QueueDepth);
        Assert.Equal((uint)2, snapshot.Imus[0].DroppedBatches);

        Assert.True(snapshot.Gps.IsActive);
        Assert.True(snapshot.Gps.HasData);
        Assert.True(snapshot.Gps.PreviewState.IsReady);
        Assert.Equal(48.2, snapshot.Gps.Latitude);
        Assert.Equal(16.3, snapshot.Gps.Longitude);
        Assert.Equal((byte)9, snapshot.Gps.Satellites);
        Assert.Equal((uint)1, snapshot.Gps.QueueDepth);
    }

    [Fact]
    public void Reset_ClearsAcceptedSessionAndLatestData()
    {
        var state = new LiveDaqSessionState();
        var sessionHeader = LiveProtocolTestFrames.CreateSessionHeaderModel(sessionId: 701);
        state.ApplyFrame(new LiveSessionHeaderFrame(CreateHeader(LiveFrameType.SessionHeader, 1), sessionHeader));
        state.ApplyFrame(new LiveTravelBatchFrame(
            CreateHeader(LiveFrameType.TravelBatch, 2),
            new LiveBatchHeader(701, 0, 10, 123456789, 1),
            [new LiveTravelRecord(100, 200)]));

        state.Reset();

        var snapshot = state.CreateSnapshot(LiveConnectionState.Disconnected, null);

        Assert.Null(snapshot.Session.SessionId);
        Assert.False(snapshot.Travel.HasData);
        Assert.Empty(snapshot.Imus);
        Assert.False(snapshot.Gps.HasData);
    }

    private static LiveFrameHeader CreateHeader(LiveFrameType frameType, uint sequence) =>
        new(LiveProtocolConstants.Magic, LiveProtocolConstants.Version, frameType, 0, sequence);
}