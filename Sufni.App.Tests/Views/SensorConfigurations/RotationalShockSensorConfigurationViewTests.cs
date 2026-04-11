using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.LinkageParts;
using Sufni.App.ViewModels.SensorConfigurations;
using Sufni.App.Views.SensorConfigurations;
using Sufni.Kinematics;

namespace Sufni.App.Tests.Views.SensorConfigurations;

public class RotationalShockSensorConfigurationViewTests
{
    [AvaloniaFact]
    public async Task RotationalShockSensorConfigurationView_ResolvesConfiguredJointSelections()
    {
        var joints = new[]
        {
            new JointViewModel("Main pivot", JointType.Fixed, 0, 0),
            new JointViewModel("Shock eye 1", JointType.Floating, 10, 5),
            new JointViewModel("Shock eye 2", JointType.Floating, 20, 8),
            new JointViewModel("Seatstay", JointType.Floating, 30, 13)
        };

        var view = new RotationalShockSensorConfigurationView
        {
            DataContext = new RotationalShockSensorConfigurationViewModel(
                new RotationalShockSensorConfiguration
                {
                    CentralJoint = "Shock eye 1",
                    AdjacentJoint1 = "Main pivot",
                    AdjacentJoint2 = "Shock eye 2"
                },
                joints)
        };

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

            Assert.Equal("Shock eye 1", Assert.IsType<JointViewModel>(sensorJoint!.SelectedItem).Name);
            Assert.Equal("Main pivot", Assert.IsType<JointViewModel>(adjacentJoint1!.SelectedItem).Name);
            Assert.Equal("Shock eye 2", Assert.IsType<JointViewModel>(adjacentJoint2!.SelectedItem).Name);
            Assert.DoesNotContain(((System.Collections.IEnumerable)sensorJoint.ItemsSource!).Cast<object>(), item => ReferenceEquals(item, adjacentJoint1.SelectedItem));
            Assert.DoesNotContain(((System.Collections.IEnumerable)sensorJoint.ItemsSource!).Cast<object>(), item => ReferenceEquals(item, adjacentJoint2.SelectedItem));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}