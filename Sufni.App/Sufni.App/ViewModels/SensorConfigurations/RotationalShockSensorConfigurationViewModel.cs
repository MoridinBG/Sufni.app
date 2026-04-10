using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.ViewModels.LinkageParts;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class RotationalShockSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private RotationalShockSensorConfiguration sensorConfiguration;

    #region Observable properties

    [ObservableProperty] private JointViewModel? sensorJoint;
    [ObservableProperty] private JointViewModel? adjacentJoint1;
    [ObservableProperty] private JointViewModel? adjacentJoint2;

    private IReadOnlyList<JointViewModel> jointViewModels = [];
    private bool initialResolutionDone;
    public IReadOnlyList<JointViewModel> JointViewModels
    {
        get => jointViewModels;
        set
        {
            jointViewModels = value;

            if (!initialResolutionDone && value.Count > 0)
            {
                // First non-empty joint list - initial load.
                SensorJoint = value.FirstOrDefault(jvm => jvm.Name == sensorConfiguration.CentralJoint);
                AdjacentJoint1 = value.FirstOrDefault(jvm => jvm.Name == sensorConfiguration.AdjacentJoint1);
                AdjacentJoint2 = value.FirstOrDefault(jvm => jvm.Name == sensorConfiguration.AdjacentJoint2);
                initialResolutionDone = true;
            }
            else if (initialResolutionDone)
            {
                // User picked a different bike
                SensorJoint = null;
                AdjacentJoint1 = null;
                AdjacentJoint2 = null;
            }
            // else: empty list before any real one — wait for the real one.

            UpdateJointsForSensorPlacement();
        }
    }

    public ObservableCollection<JointViewModel> JointsForSensor { get; } = [];
    public ObservableCollection<JointViewModel> JointsForAdjacent1 { get; } = [];
    public ObservableCollection<JointViewModel> JointsForAdjacent2 { get; } = [];

    #endregion Observable properties

    #region Property change handlers

    // Make sure sensor placement joint comboboxes are mutually exclusive.
    partial void OnSensorJointChanged(JointViewModel? value) => UpdateJointsForSensorPlacement();
    partial void OnAdjacentJoint1Changed(JointViewModel? value) => UpdateJointsForSensorPlacement();
    partial void OnAdjacentJoint2Changed(JointViewModel? value) => UpdateJointsForSensorPlacement();

    #endregion Property change handlers

    #region SensorConfigurationViewModel overrides

    public override void EvaluateDirtiness()
    {
        IsDirty = SensorJoint?.Name != sensorConfiguration.CentralJoint ||
                  AdjacentJoint1?.Name != sensorConfiguration.AdjacentJoint1 ||
                  AdjacentJoint2?.Name != sensorConfiguration.AdjacentJoint2;
    }

    public override bool CanSave()
    {
        return SensorJoint is not null && AdjacentJoint1 is not null && AdjacentJoint2 is not null;
    }

    public override void Save()
    {
        Debug.Assert(SensorJoint is not null);
        Debug.Assert(AdjacentJoint1 is not null);
        Debug.Assert(AdjacentJoint2 is not null);

        sensorConfiguration = new RotationalShockSensorConfiguration
        {
            CentralJoint = SensorJoint.Name,
            AdjacentJoint1 = AdjacentJoint1.Name,
            AdjacentJoint2 = AdjacentJoint2.Name
        };

        EvaluateDirtiness();
    }

    public override string ToJson()
    {
        Debug.Assert(SensorJoint is not null);
        Debug.Assert(AdjacentJoint1 is not null);
        Debug.Assert(AdjacentJoint2 is not null);

        var sc = new RotationalShockSensorConfiguration
        {
            CentralJoint = SensorJoint.Name,
            AdjacentJoint1 = AdjacentJoint1.Name,
            AdjacentJoint2 = AdjacentJoint2.Name
        };

        return SensorConfiguration.ToJson(sc);
    }

    #endregion SensorConfigurationViewModel overrides

    #region Constructors

    public RotationalShockSensorConfigurationViewModel(IReadOnlyList<JointViewModel>? joints) : this(new RotationalShockSensorConfiguration(), joints) { }

    public RotationalShockSensorConfigurationViewModel(RotationalShockSensorConfiguration configuration, IReadOnlyList<JointViewModel>? joints)
    {
        Type = SensorType.RotationalShock;
        sensorConfiguration = configuration;

        if (joints is null) return;
        // The JointViewModels setter performs the initial resolution
        // from configuration.CentralJoint / AdjacentJoint1 / AdjacentJoint2.
        JointViewModels = joints;
    }

    #endregion Constructors

    #region Private methods

    private static void UpdateCollection(ObservableCollection<JointViewModel> target, List<JointViewModel> newItems)
    {
        // Remove items not in the new list
        for (var i = target.Count - 1; i >= 0; i--)
        {
            if (!newItems.Contains(target[i]))
                target.RemoveAt(i);
        }

        // Add missing new items
        foreach (var item in newItems)
        {
            if (!target.Contains(item))
                target.Add(item);
        }
    }

    private void UpdateJointsForSensorPlacement()
    {
        var selected = new HashSet<JointViewModel?> { SensorJoint, AdjacentJoint1, AdjacentJoint2 };

        UpdateCollection(JointsForSensor, GetFiltered(SensorJoint));
        UpdateCollection(JointsForAdjacent1, GetFiltered(AdjacentJoint1));
        UpdateCollection(JointsForAdjacent2, GetFiltered(AdjacentJoint2));
        return;

        List<JointViewModel> GetFiltered(JointViewModel? current) =>
            [.. JointViewModels.Where(item => item == current || !selected.Contains(item))];
    }

    #endregion Private methods
}