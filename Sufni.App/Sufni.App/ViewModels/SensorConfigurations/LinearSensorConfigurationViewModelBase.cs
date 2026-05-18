using CommunityToolkit.Mvvm.ComponentModel;

namespace Sufni.App.ViewModels.SensorConfigurations;

public abstract partial class LinearSensorConfigurationViewModelBase
    : SensorConfigurationViewModel, ILinearSensorConfigurationViewModel
{
    private double savedLength;
    private int savedResolution;

    #region Observable properties

    [ObservableProperty] private double? length;
    [ObservableProperty] private int? resolution;

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
