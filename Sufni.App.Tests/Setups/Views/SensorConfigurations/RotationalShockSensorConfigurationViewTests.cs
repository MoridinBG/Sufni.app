using Sufni.App.Setups.Coordinators;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.Kinematics;

using Sufni.App.Setups.Views.SensorConfigurations;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.ViewModels.SensorConfigurations;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Setups.Views.SensorConfigurations;

[Collection("Ui")]
public class RotationalShockSensorConfigurationViewTests
{
    [AvaloniaFact]
    public async Task RotationalShockSensorConfigurationView_ResolvesConfiguredJointSelections()
    {
        var view = CreateView();

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var sensorJoint = view.FindControl<ComboBox>("SensorJointComboBox");
            var adjacentJoint1 = view.FindControl<ComboBox>("AdjacentJoint1ComboBox");
            var adjacentJoint2 = view.FindControl<ComboBox>("AdjacentJoint2ComboBox");
            Assert.NotNull(sensorJoint);
            Assert.NotNull(adjacentJoint1);
            Assert.NotNull(adjacentJoint2);

            Assert.Equal("Shock eye 1", Assert.IsType<JointSpec>(sensorJoint!.SelectedItem).Name);
            Assert.Equal("Main pivot", Assert.IsType<JointSpec>(adjacentJoint1!.SelectedItem).Name);
            Assert.Equal("Shock eye 2", Assert.IsType<JointSpec>(adjacentJoint2!.SelectedItem).Name);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task RotationalShockSensorConfigurationView_SensorJointComboBox_ExcludesAdjacentJoints()
    {
        var view = CreateView();

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var sensorJoint = view.FindControl<ComboBox>("SensorJointComboBox");
            var adjacentJoint1 = view.FindControl<ComboBox>("AdjacentJoint1ComboBox");
            var adjacentJoint2 = view.FindControl<ComboBox>("AdjacentJoint2ComboBox");
            Assert.NotNull(sensorJoint);
            Assert.NotNull(adjacentJoint1);
            Assert.NotNull(adjacentJoint2);

            Assert.DoesNotContain(((System.Collections.IEnumerable)sensorJoint!.ItemsSource!).Cast<object>(), item => ReferenceEquals(item, adjacentJoint1!.SelectedItem));
            Assert.DoesNotContain(((System.Collections.IEnumerable)sensorJoint.ItemsSource!).Cast<object>(), item => ReferenceEquals(item, adjacentJoint2!.SelectedItem));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static RotationalShockSensorConfigurationView CreateView()
    {
        return new RotationalShockSensorConfigurationView
        {
            DataContext = new RotationalShockSensorConfigurationViewModel(
                new SensorDraft.RotationalShock("Shock eye 1", "Main pivot", "Shock eye 2"),
                [
                    new JointSpec("Main pivot", JointType.Fixed, 0, 0),
                    new JointSpec("Shock eye 1", JointType.Floating, 10, 5),
                    new JointSpec("Shock eye 2", JointType.Floating, 20, 8),
                    new JointSpec("Seatstay", JointType.Floating, 30, 13)
                ])
        };
    }
}