using Avalonia;
using Avalonia.Headless.XUnit;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.Editors;
using Sufni.Kinematics;

namespace Sufni.App.Tests.ViewModels.Editors;

public class BikeWheelGeometryViewModelTests
{
    private static double WheelDiameter(EtrtoRimSize rimSize, double tireWidth) =>
        Math.Round(rimSize.CalculateTotalDiameterMm(tireWidth), 1);

    private static BikeSnapshot SnapshotWithWheels() => TestSnapshots.Bike() with
    {
        FrontWheelRimSize = EtrtoRimSize.Inch29,
        FrontWheelTireWidth = 2.4,
        FrontWheelDiameterMm = WheelDiameter(EtrtoRimSize.Inch29, 2.4),
        RearWheelRimSize = EtrtoRimSize.Inch275,
        RearWheelTireWidth = 2.5,
        RearWheelDiameterMm = WheelDiameter(EtrtoRimSize.Inch275, 2.5),
    };

    [AvaloniaFact]
    public void ApplySnapshot_CopiesWheelInputs_AndMatchesSnapshot()
    {
        var snapshot = SnapshotWithWheels();
        var viewModel = new BikeWheelGeometryViewModel();

        viewModel.ApplySnapshot(snapshot);

        Assert.Equal(snapshot.FrontWheelRimSize, viewModel.FrontWheelRimSize);
        Assert.Equal(snapshot.FrontWheelTireWidth, viewModel.FrontWheelTireWidth);
        Assert.Equal(snapshot.FrontWheelDiameterMm, viewModel.FrontWheelDiameter);
        Assert.Equal(snapshot.RearWheelRimSize, viewModel.RearWheelRimSize);
        Assert.Equal(snapshot.RearWheelTireWidth, viewModel.RearWheelTireWidth);
        Assert.Equal(snapshot.RearWheelDiameterMm, viewModel.RearWheelDiameter);
        Assert.False(viewModel.HasChangesComparedTo(snapshot));
    }

    [AvaloniaFact]
    public void SettingFrontWheelRimAndTire_ComputesDiameter_AndPreservesInputs()
    {
        var viewModel = new BikeWheelGeometryViewModel();

        viewModel.FrontWheelRimSize = EtrtoRimSize.Inch29;
        viewModel.FrontWheelTireWidth = 2.4;

        Assert.Equal(EtrtoRimSize.Inch29, viewModel.FrontWheelRimSize);
        Assert.Equal(2.4, viewModel.FrontWheelTireWidth);
        Assert.Equal(WheelDiameter(EtrtoRimSize.Inch29, 2.4), viewModel.FrontWheelDiameter);
    }

    [AvaloniaFact]
    public void SettingFrontWheelDiameterManually_ClearsDerivedRimAndTireState()
    {
        var viewModel = new BikeWheelGeometryViewModel
        {
            FrontWheelRimSize = EtrtoRimSize.Inch29,
            FrontWheelTireWidth = 2.4,
        };

        viewModel.FrontWheelDiameter = 700;

        Assert.Null(viewModel.FrontWheelRimSize);
        Assert.Null(viewModel.FrontWheelTireWidth);
        Assert.Equal(700, viewModel.FrontWheelDiameter);
    }

    [AvaloniaFact]
    public void RefreshDerived_ComputesWheelCircleMetrics_FromCentersAndScale()
    {
        var viewModel = new BikeWheelGeometryViewModel
        {
            FrontWheelDiameter = 760,
            RearWheelDiameter = 750,
        };

        viewModel.RefreshDerived(new Point(100, 200), new Point(20, 40), 2);

        Assert.True(viewModel.HasWheels);
        Assert.Equal(190, viewModel.FrontWheelRadiusPixels);
        Assert.Equal(187.5, viewModel.RearWheelRadiusPixels);
        Assert.Equal(-90, viewModel.FrontWheelCircleLeft);
        Assert.Equal(10, viewModel.FrontWheelCircleTop);
        Assert.Equal(375, viewModel.RearWheelCircleDiameter);
    }

    [AvaloniaFact]
    public void TryComputeGroundAlignmentDelta_ReturnsNull_WhenInputsAreIncomplete()
    {
        var viewModel = new BikeWheelGeometryViewModel();

        var delta = viewModel.TryComputeGroundAlignmentDelta(null, new Point(10, 20), 1);

        Assert.Null(delta);
    }

    [AvaloniaFact]
    public void HasChangesComparedTo_ReturnsTrue_WhenWheelInputChanges()
    {
        var snapshot = SnapshotWithWheels();
        var viewModel = new BikeWheelGeometryViewModel();
        viewModel.ApplySnapshot(snapshot);

        viewModel.RearWheelDiameter = 712;

        Assert.True(viewModel.HasChangesComparedTo(snapshot));
    }
}