using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SensorConfigurations;
using Sufni.App.Views.SensorConfigurations;

namespace Sufni.App.Tests.Views.SensorConfigurations;

public class RotationalForkSensorConfigurationViewTests
{
    [AvaloniaFact]
    public async Task RotationalForkSensorConfigurationView_MaxLengthNumericUpDown_DisplaysBoundValue()
    {
        var view = new RotationalForkSensorConfigurationView
        {
            DataContext = new RotationalForkSensorConfigurationViewModel(new RotationalForkSensorConfiguration
            {
                MaxLength = 218.4,
                ArmLength = 82.6
            })
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var maxLength = view.FindControl<NumericUpDown>("MaxLengthNumericUpDown");
            Assert.NotNull(maxLength);
            Assert.Equal(218.4, Convert.ToDouble(maxLength!.Value));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task RotationalForkSensorConfigurationView_ArmLengthNumericUpDown_DisplaysBoundValue()
    {
        var view = new RotationalForkSensorConfigurationView
        {
            DataContext = new RotationalForkSensorConfigurationViewModel(new RotationalForkSensorConfiguration
            {
                MaxLength = 218.4,
                ArmLength = 82.6
            })
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var armLength = view.FindControl<NumericUpDown>("ArmLengthNumericUpDown");
            Assert.NotNull(armLength);
            Assert.Equal(82.6, Convert.ToDouble(armLength!.Value));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}