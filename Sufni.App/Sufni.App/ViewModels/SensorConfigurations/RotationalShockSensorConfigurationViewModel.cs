using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.ViewModels.Items;
using Sufni.App.ViewModels.LinkageParts;

namespace Sufni.App.ViewModels.SensorConfigurations;

public partial class RotationalShockSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private readonly RotationalShockSensorConfiguration sensorConfiguration;
    
    [ObservableProperty] private JointViewModel? sensorJoint;
    [ObservableProperty] private JointViewModel? adjacentJoint1;
    [ObservableProperty] private JointViewModel? adjacentJoint2;

    private ObservableCollection<JointViewModel> jointViewModels = [];
    public ObservableCollection<JointViewModel> JointViewModels
    {
        get => jointViewModels;
        set
        {
            jointViewModels = value;
            UpdateJointsForSensorPlacement();
        }
    }

    public ObservableCollection<JointViewModel> JointsForSensor { get; } = [];
    public ObservableCollection<JointViewModel> JointsForAdjacent1 { get; } = [];
    public ObservableCollection<JointViewModel> JointsForAdjacent2 { get; } = [];

    // Make sure sensor placement joint comboboxes are mutually exclusive.
    partial void OnSensorJointChanged(JointViewModel? value) => UpdateJointsForSensorPlacement();
    partial void OnAdjacentJoint1Changed(JointViewModel? value) => UpdateJointsForSensorPlacement();
    partial void OnAdjacentJoint2Changed(JointViewModel? value) => UpdateJointsForSensorPlacement();

    protected override void EvaluateDirtiness()
    {
        IsDirty = SensorJoint?.Name != sensorConfiguration.CentralJoint ||
                  AdjacentJoint1?.Name != sensorConfiguration.AdjacentJoint1 ||
                  AdjacentJoint2?.Name != sensorConfiguration.AdjacentJoint2;
    }

    public override bool CanSave()
    {
        return SensorJoint is not null && AdjacentJoint1 is not null && AdjacentJoint2 is not null;
    }

    public RotationalShockSensorConfigurationViewModel(BikeViewModel? bikeViewModel) : this(new RotationalShockSensorConfiguration(), bikeViewModel) { }

    public RotationalShockSensorConfigurationViewModel(RotationalShockSensorConfiguration configuration, BikeViewModel? bikeViewModel)
    {
        Type = SensorType.RotationalShock;
        sensorConfiguration = configuration;

        if  (bikeViewModel is null) return;
        JointViewModels = bikeViewModel.JointViewModels;
        SensorJoint = bikeViewModel.JointViewModels.FirstOrDefault(jvm => jvm.Name == configuration.CentralJoint);
        AdjacentJoint1 = bikeViewModel.JointViewModels.FirstOrDefault(jvm => jvm.Name == configuration.AdjacentJoint1);
        AdjacentJoint2 = bikeViewModel.JointViewModels.FirstOrDefault(jvm => jvm.Name == configuration.AdjacentJoint2);
    }

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

        return JsonSerializer.Serialize(sc, SensorConfiguration.SerializerOptions);
    }
}