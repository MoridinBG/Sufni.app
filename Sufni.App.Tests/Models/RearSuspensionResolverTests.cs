using Sufni.App.Models;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Models;

public class RearSuspensionResolverTests
{
    [Fact]
    public void None_NoPayloads_ReturnsHardtail()
    {
        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.None, linkage: null, leverageRatio: null);

        Assert.IsType<RearSuspensionResolution.Hardtail>(result);
    }

    [Fact]
    public void Linkage_WithLinkagePayload_ReturnsLinkage()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage();

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.Linkage, linkage, leverageRatio: null);

        var resolved = Assert.IsType<RearSuspensionResolution.Linkage>(result);
        Assert.Same(linkage, resolved.Value.Linkage);
    }

    [Fact]
    public void LeverageRatio_WithLeverageRatioPayload_ReturnsLeverageRatio()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25), (20, 50));

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.LeverageRatio, linkage: null, leverageRatio);

        var resolved = Assert.IsType<RearSuspensionResolution.LeverageRatio>(result);
        Assert.Same(leverageRatio, resolved.Value.LeverageRatio);
    }

    [Fact]
    public void None_WithLinkagePayload_ReturnsKindNoneHasPayload()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage();

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.None, linkage, leverageRatio: null);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.KindNoneHasPayload, invalid.Error);
    }

    [Fact]
    public void None_WithLeverageRatioPayload_ReturnsKindNoneHasPayload()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.None, linkage: null, leverageRatio);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.KindNoneHasPayload, invalid.Error);
    }

    [Fact]
    public void Linkage_MissingPayload_ReturnsKindLinkageMissingPayload()
    {
        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.Linkage, linkage: null, leverageRatio: null);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.KindLinkageMissingPayload, invalid.Error);
    }

    [Fact]
    public void Linkage_WithLeverageRatioPayload_ReturnsKindLinkageHasLeverageRatioPayload()
    {
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.Linkage, linkage: null, leverageRatio);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.KindLinkageHasLeverageRatioPayload, invalid.Error);
    }

    [Fact]
    public void LeverageRatio_MissingPayload_ReturnsKindLeverageRatioMissingPayload()
    {
        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.LeverageRatio, linkage: null, leverageRatio: null);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.KindLeverageRatioMissingPayload, invalid.Error);
    }

    [Fact]
    public void LeverageRatio_WithLinkagePayload_ReturnsKindLeverageRatioHasLinkagePayload()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage();

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.LeverageRatio, linkage, leverageRatio: null);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.KindLeverageRatioHasLinkagePayload, invalid.Error);
    }

    [Fact]
    public void Linkage_WithBothPayloads_ReturnsMultiplePayloadsPresent()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage();
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.Linkage, linkage, leverageRatio);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.MultiplePayloadsPresent, invalid.Error);
    }

    [Fact]
    public void LeverageRatio_WithBothPayloads_ReturnsMultiplePayloadsPresent()
    {
        var linkage = TestSnapshots.FullSuspensionLinkage();
        var leverageRatio = TestSnapshots.LeverageRatioCurve((0, 0), (10, 25));

        var result = RearSuspensionResolver.Resolve(RearSuspensionKind.LeverageRatio, linkage, leverageRatio);

        var invalid = Assert.IsType<RearSuspensionResolution.Invalid>(result);
        Assert.Equal(RearSuspensionResolutionError.MultiplePayloadsPresent, invalid.Error);
    }
}
