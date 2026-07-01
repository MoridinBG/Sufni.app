using CommunityToolkit.Mvvm.ComponentModel;

using Sufni.App.Shared.Common;
namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public abstract partial class LinearSensorConfigurationViewModelBase
    : SensorConfigurationViewModel, ILinearSensorConfigurationViewModel
{
    private double savedLength;
    private int savedResolution;

    #region Observable properties

    [ObservableProperty] public partial double? Length { get; set; }
    [ObservableProperty] public partial int? Resolution { get; set; }

    #endregion Observable properties

    public override void EvaluateDirtiness()
    {
        IsDirty = !MathUtils.AreEqual(Length, savedLength) ||
                  Resolution != savedResolution;
    }

    public override bool CanSave()
    {
        return Length is not null && Resolution is not null;
    }

    protected void LoadLinearValues(double length, int resolution)
    {
        savedLength = length;
        savedResolution = resolution;
        Length = length;
        Resolution = resolution;
        EvaluateDirtiness();
    }

    protected void AcceptSavedLinearValues(double length, int resolution)
    {
        savedLength = length;
        savedResolution = resolution;
        EvaluateDirtiness();
    }
}
