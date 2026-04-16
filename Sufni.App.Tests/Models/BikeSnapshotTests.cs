using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Models;

public class BikeSnapshotTests
{
    [Fact]
    public void TryResolveRearSuspension_ReturnsNull_ForHardtailSnapshot()
    {
        var snapshot = TestSnapshots.Bike();

        var success = snapshot.TryResolveRearSuspension(out var rearSuspension, out var errorMessage);

        Assert.True(success);
        Assert.Null(rearSuspension);
        Assert.Null(errorMessage);
    }

    [Fact]
    public void TryResolveRearSuspension_ReturnsLeverageRatioRearSuspension_WhenSnapshotMatchesKind()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50));
        var snapshot = TestSnapshots.LeverageRatioBike(leverageRatio);

        var success = snapshot.TryResolveRearSuspension(out var rearSuspension, out var errorMessage);

        Assert.True(success);
        Assert.Null(errorMessage);
        var resolved = Assert.IsType<LeverageRatioRearSuspension>(rearSuspension);
        Assert.Equal(leverageRatio.Points, resolved.LeverageRatio.Points);
    }

    [Fact]
    public void FromSnapshot_RestoresLeverageRatioBike()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50));
        var snapshot = TestSnapshots.LeverageRatioBike(leverageRatio, shockStroke: 20, updated: 7);

        var restored = Bike.FromSnapshot(snapshot);

        Assert.Equal(RearSuspensionKind.LeverageRatio, restored.RearSuspensionKind);
        Assert.Equal(20, restored.ShockStroke);
        Assert.NotNull(restored.LeverageRatio);
        Assert.Equal(leverageRatio.Points, restored.LeverageRatio!.Points);
        Assert.Equal(7, restored.Updated);
    }
}