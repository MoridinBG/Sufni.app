using Sufni.App.Setups.Coordinators;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

using Sufni.App.Bikes.ViewModels.LinkageParts;
using Sufni.App.Setups.Models.SensorConfigurations;
namespace Sufni.App.Setups.ViewModels.SensorConfigurations;

public partial class RotationalShockSensorConfigurationViewModel : SensorConfigurationViewModel
{
    private SensorDraft.RotationalShock? initialDraft;

    #region Observable properties

    [ObservableProperty] public partial JointViewModel? SensorJoint { get; set; }
    [ObservableProperty] public partial JointViewModel? AdjacentJoint1 { get; set; }
    [ObservableProperty] public partial JointViewModel? AdjacentJoint2 { get; set; }

    private IReadOnlyList<JointViewModel> jointViewModels = [];
    public IReadOnlyList<JointViewModel> JointViewModels
    {
        get => jointViewModels;
        set
        {
            jointViewModels = value;

            if (initialDraft is { } draft && value.Count > 0)
            {
                // First non-empty joint list - initial load.
                SensorJoint = value.FirstOrDefault(jvm => jvm.Name == draft.CentralJoint);
                AdjacentJoint1 = value.FirstOrDefault(jvm => jvm.Name == draft.AdjacentJoint1);
                AdjacentJoint2 = value.FirstOrDefault(jvm => jvm.Name == draft.AdjacentJoint2);
                initialDraft = null;
            }
            else if (initialDraft is null)
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

    public override SensorDraft CaptureDraft() => initialDraft ??
        new SensorDraft.RotationalShock(SensorJoint?.Name, AdjacentJoint1?.Name, AdjacentJoint2?.Name);

    public RotationalShockSensorConfigurationViewModel(SensorDraft.RotationalShock draft, IReadOnlyList<JointViewModel>? joints)
    {
        Type = draft.Type;
        initialDraft = draft;
        if (joints is not null) JointViewModels = joints;
    }

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