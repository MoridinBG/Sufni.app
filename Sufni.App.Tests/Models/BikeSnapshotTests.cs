using System;
using Sufni.App.Models;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Models;

public class BikeSnapshotTests
{
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
    public void From_PreservesDampingSpeedCutoffs()
    {
        var bike = new Bike(Guid.NewGuid(), "cutoffs")
        {
            FrontCompressionDampingCutoffMmPerSecond = 120,
            FrontReboundDampingCutoffMmPerSecond = 130,
            RearCompressionDampingCutoffMmPerSecond = 240,
            RearReboundDampingCutoffMmPerSecond = 250,
        };

        var snapshot = BikeSnapshot.From(bike);

        Assert.Equal(120, snapshot.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(130, snapshot.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(240, snapshot.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(250, snapshot.RearReboundDampingCutoffMmPerSecond);
    }
}
