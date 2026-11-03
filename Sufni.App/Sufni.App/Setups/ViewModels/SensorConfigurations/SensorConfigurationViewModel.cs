using Sufni.Kinematics;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Setups.Coordinators;
using Sufni.App.Setups.Models.SensorConfigurations;

namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public abstract partial class SensorConfigurationViewModel : ObservableObject
{
    public SensorType Type { get; protected set; }
    public abstract SensorDraft CaptureDraft();

    public static SensorConfigurationViewModel? Create(SensorType? type, IReadOnlyList<JointSpec>? joints = null) =>
        FromDraft(type switch
        {
            SensorType.LinearFork => new SensorDraft.LinearFork(0, 0),
            SensorType.RotationalFork => new SensorDraft.RotationalFork(0, 0),
            SensorType.LinearShock => new SensorDraft.LinearShock(0, 0),
            SensorType.LinearShockStroke => new SensorDraft.LinearShock(0, 0, true),
            SensorType.RotationalShock => new SensorDraft.RotationalShock(null, null, null),
            _ => null,
        }, joints);

    public static SensorConfigurationViewModel? FromDraft(SensorDraft? draft, IReadOnlyList<JointSpec>? joints = null) => draft switch
    {
        SensorDraft.LinearFork value => new LinearForkSensorConfigurationViewModel(value),
        SensorDraft.LinearShock value => new LinearShockSensorConfigurationViewModel(value),
        SensorDraft.RotationalFork value => new RotationalForkSensorConfigurationViewModel(value),
        SensorDraft.RotationalShock value => new RotationalShockSensorConfigurationViewModel(value, joints),
        _ => null,
    };
}
