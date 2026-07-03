using System;

using Sufni.App.ExtensionHost.Contracts.SessionDetails;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.Stores;
using Sufni.App.Tests.TestSupport.Fixtures;
namespace Sufni.App.Tests.Bikes.Stores;

public class BikeSnapshotTests
{
    [Fact]
    public void From_ExposesRearSuspensionConvenienceAccessors()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));
        var bike = new Bike(Guid.NewGuid(), "leverage ratio")
        {
            HeadAngle = 65,
            ForkStroke = 160,
            RearSuspension = new RearSuspensionSpec.LeverageRatio(leverageRatio),
            Updated = 1,
        };

        var snapshot = BikeSnapshot.From(bike);

        Assert.Equal(RearSuspensionKind.LeverageRatio, snapshot.Kind);
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

        Assert.Equal(DampingSpeedCutoffs.FromValues(120, 130, 240, 250), snapshot.DampingSpeedCutoffs);
        Assert.Equal(120, snapshot.FrontCompressionDampingCutoffMmPerSecond);
        Assert.Equal(130, snapshot.FrontReboundDampingCutoffMmPerSecond);
        Assert.Equal(240, snapshot.RearCompressionDampingCutoffMmPerSecond);
        Assert.Equal(250, snapshot.RearReboundDampingCutoffMmPerSecond);
    }

    [Fact]
    public void From_CopiesImageBytes()
    {
        var bike = new Bike(Guid.NewGuid(), "image")
        {
            ImageBytes = [1, 2, 3],
        };

        var snapshot = BikeSnapshot.From(bike);

        bike.ImageBytes[0] = 9;

        Assert.Equal([1, 2, 3], snapshot.ImageBytes);
    }

    [Fact]
    public void ImageBytes_AreCopiedOnInitAndRead()
    {
        byte[] imageBytes = [1, 2, 3];
        var snapshot = TestSnapshots.Bike() with
        {
            ImageBytes = imageBytes,
        };

        imageBytes[0] = 9;
        var exposed = snapshot.ImageBytes;
        exposed[1] = 8;

        Assert.Equal([1, 2, 3], snapshot.ImageBytes);
    }

    [Fact]
    public void ImageByteCount_DoesNotRequireImageBytesGetter()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            ImageBytes = [1, 2, 3],
        };

        Assert.Equal(3, snapshot.ImageByteCount);
    }

    [Fact]
    public void ImageBytesSpan_ExposesReadOnlySnapshotBytesWithoutMutatingSnapshot()
    {
        var snapshot = TestSnapshots.Bike() with
        {
            ImageBytes = [1, 2, 3],
        };

        Assert.True(snapshot.ImageBytesSpan.SequenceEqual(new byte[] { 1, 2, 3 }));

        var exposed = snapshot.ImageBytes;
        exposed[0] = 9;

        Assert.True(snapshot.ImageBytesSpan.SequenceEqual(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void WithExpression_ReplacesLinkageSpecWithoutMutatingOriginalSnapshot()
    {
        var linkage = TestSnapshots.FullSuspensionLinkageSpec();
        var snapshot = TestSnapshots.Bike() with
        {
            ShockStroke = linkage.ShockStroke,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage)
        };

        var updated = snapshot with
        {
            ShockStroke = 0.75,
            RearSuspension = new RearSuspensionSpec.Linkage(linkage.WithShockStroke(0.75))
        };

        Assert.Same(linkage, snapshot.Linkage);
        Assert.Equal(linkage.ShockStroke, snapshot.ShockStroke);
        Assert.Equal(linkage.ShockStroke, Assert.IsType<RearSuspensionSpec.Linkage>(snapshot.RearSuspension).Spec.ShockStroke);
        Assert.Equal(0.75, updated.ShockStroke);
        Assert.Equal(0.75, Assert.IsType<RearSuspensionSpec.Linkage>(updated.RearSuspension).Spec.ShockStroke);
    }
}
