using Sufni.App.Video.Models;
using Sufni.App.Video.Stores;

namespace Sufni.App.Tests.Video.Models;

public class SessionVideoSelectionTests
{
    private const long SessionTimestamp = 1_000;

    [Fact]
    public void Select_ReturnsClipContainingTelemetryTime()
    {
        var early = Clip(startTimestamp: SessionTimestamp, durationSeconds: 10);
        var late = Clip(startTimestamp: SessionTimestamp + 10, durationSeconds: 10);

        var selected = SessionVideoSelection.Select([early, late], SessionTimestamp, telemetrySeconds: 15);

        Assert.Equal(late, selected);
    }

    [Fact]
    public void Select_PrefersLatestAddedClip_WhenClipsOverlap()
    {
        var older = Clip(startTimestamp: SessionTimestamp, durationSeconds: 30, added: 1);
        var newer = Clip(startTimestamp: SessionTimestamp, durationSeconds: 30, added: 2);

        var selected = SessionVideoSelection.Select([newer, older], SessionTimestamp, telemetrySeconds: 5);

        Assert.Equal(newer, selected);
    }

    [Fact]
    public void Select_ReturnsNearestClip_WhenNoClipContainsTelemetryTime()
    {
        var far = Clip(startTimestamp: SessionTimestamp + 100, durationSeconds: 10);
        var near = Clip(startTimestamp: SessionTimestamp + 20, durationSeconds: 10);

        var selected = SessionVideoSelection.Select([far, near], SessionTimestamp, telemetrySeconds: 5);

        Assert.Equal(near, selected);
    }

    [Fact]
    public void Select_AppliesAlignmentOffset()
    {
        // Without the offset the clip starts 10 s after the telemetry time.
        var aligned = Clip(startTimestamp: SessionTimestamp + 10, durationSeconds: 5, alignmentOffsetSeconds: 12);
        var unaligned = Clip(startTimestamp: SessionTimestamp + 8, durationSeconds: 1, added: 1);

        var selected = SessionVideoSelection.Select([aligned, unaligned], SessionTimestamp, telemetrySeconds: 0);

        Assert.Equal(aligned, selected);
    }

    [Fact]
    public void Select_SkipsClipsWhoseFileIsMissing()
    {
        var missing = Clip(startTimestamp: SessionTimestamp, durationSeconds: 30, fileExists: false);

        var selected = SessionVideoSelection.Select([missing], SessionTimestamp, telemetrySeconds: 5);

        Assert.Null(selected);
    }

    private static SessionVideoSnapshot Clip(
        long startTimestamp,
        double durationSeconds,
        long added = 0,
        double alignmentOffsetSeconds = 0,
        bool fileExists = true) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "/videos/clip.mp4",
            startTimestamp,
            durationSeconds,
            alignmentOffsetSeconds,
            added,
            fileExists);
}
