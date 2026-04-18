using Avalonia;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors.Bike;
using Sufni.Kinematics;

namespace Sufni.App.Tests.ViewModels.Editors;

public class BikeImageCanvasViewModelTests
{
    [AvaloniaFact]
    public void ApplySnapshot_CopiesImageState_AndMatchesSnapshot()
    {
        var image = TestImages.SmallPng();
        var snapshot = TestSnapshots.Bike() with
        {
            Image = image,
            ImageRotationDegrees = 12.5,
        };
        var viewModel = new BikeImageCanvasViewModel();

        viewModel.ApplySnapshot(snapshot.Image, snapshot.ImageRotationDegrees);

        Assert.Same(image, viewModel.Image);
        Assert.Equal(12.5, viewModel.ImageRotationDegrees);
        Assert.False(viewModel.HasChangesComparedTo(snapshot));
    }

    [AvaloniaFact]
    public void RotatedImage_ReusesCacheUntilRotationChanges()
    {
        var viewModel = new BikeImageCanvasViewModel();
        viewModel.ApplySnapshot(TestImages.SmallPng(), 15);

        var first = viewModel.RotatedImage;
        var second = viewModel.RotatedImage;
        viewModel.ImageRotationDegrees = 30;
        var third = viewModel.RotatedImage;

        Assert.Same(first, second);
        Assert.NotSame(second, third);
    }

    [AvaloniaFact]
    public void RefreshLayout_ComputesCanvasBounds_FromImageJointAndWheelBounds()
    {
        var viewModel = new BikeImageCanvasViewModel();
        viewModel.ApplySnapshot(TestImages.SmallPng(), 0);

        viewModel.RefreshLayout(
            new Rect(-5, 2, 10, 3),
            new Rect(6, -4, 8, 7));

        Assert.Equal(59, viewModel.CanvasWidth);
        Assert.Equal(49, viewModel.CanvasHeight);
        Assert.Equal(25, viewModel.ContentOffsetX);
        Assert.Equal(24, viewModel.ContentOffsetY);
    }

    [AvaloniaFact]
    public void RefreshRearAxlePath_ProjectsRawCoordinates_UsingImageHeightAndScale()
    {
        var viewModel = new BikeImageCanvasViewModel();
        viewModel.ApplySnapshot(TestImages.SmallPng(), 0);
        viewModel.SetRearAxlePathData(new CoordinateList([2, 4], [0.25, 0.75]));

        viewModel.RefreshRearAxlePath(1);

        Assert.Equal(2, viewModel.RearAxlePath.Count);
        Assert.Equal(2, viewModel.RearAxlePath[0].X, 3);
        Assert.Equal(0.75, viewModel.RearAxlePath[0].Y, 3);
        Assert.Equal(4, viewModel.RearAxlePath[1].X, 3);
        Assert.Equal(0.25, viewModel.RearAxlePath[1].Y, 3);
    }

    [AvaloniaFact]
    public void HasChangesComparedTo_IgnoresOverlayVisibility()
    {
        var image = TestImages.SmallPng();
        var snapshot = TestSnapshots.Bike() with
        {
            Image = image,
            ImageRotationDegrees = 0,
        };
        var viewModel = new BikeImageCanvasViewModel();
        viewModel.ApplySnapshot(snapshot.Image, snapshot.ImageRotationDegrees);

        viewModel.OverlayVisible = true;

        Assert.False(viewModel.HasChangesComparedTo(snapshot));
    }
}