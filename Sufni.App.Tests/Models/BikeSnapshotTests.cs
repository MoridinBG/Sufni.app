using System;
using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Models;

public class BikeSnapshotTests
{
    [Fact]
    public void From_CopiesRawRearSuspensionFields_ForHardtailBike()
    {
        var bike = new Bike(Guid.NewGuid(), "hardtail")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspensionKind = RearSuspensionKind.None,
            Updated = 3,
        };

        var snapshot = BikeSnapshot.From(bike);

        Assert.Equal(RearSuspensionKind.None, snapshot.RearSuspensionKind);
        Assert.Null(snapshot.Linkage);
        Assert.Null(snapshot.LeverageRatio);
        Assert.Equal(3, snapshot.Updated);
    }

    [Fact]
    public void From_CopiesRawLeverageRatioPayload()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50));
        var snapshot = TestSnapshots.LeverageRatioBike(leverageRatio, shockStroke: 20, updated: 7);

        Assert.Equal(RearSuspensionKind.LeverageRatio, snapshot.RearSuspensionKind);
        Assert.Null(snapshot.Linkage);
        Assert.NotNull(snapshot.LeverageRatio);
        Assert.Equal(leverageRatio.Points, snapshot.LeverageRatio!.Points);
        Assert.Equal(20, snapshot.ShockStroke);
        Assert.Equal(7, snapshot.Updated);
    }

    [Fact]
    public void From_PreservesKindPayloadMismatch_WithoutNormalization()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var bike = new Bike(Guid.NewGuid(), "mismatched")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspensionKind = RearSuspensionKind.Linkage,
            LeverageRatio = leverageRatio,
            Updated = 1,
        };

        var snapshot = BikeSnapshot.From(bike);

        Assert.Equal(RearSuspensionKind.Linkage, snapshot.RearSuspensionKind);
        Assert.Null(snapshot.Linkage);
        Assert.Same(leverageRatio, snapshot.LeverageRatio);
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
