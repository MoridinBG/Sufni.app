using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SensorConfigurations;
using Sufni.App.Views.SensorConfigurations;

namespace Sufni.App.Tests.Views.SensorConfigurations;

public class LinearShockSensorConfigurationViewTests
{
    [AvaloniaFact]
    public async Task LinearShockSensorConfigurationView_LengthNumericUpDown_DisplaysBoundValue()
    {
        var view = new LinearShockSensorConfigurationView
        {
            DataContext = new LinearShockSensorConfigurationViewModel(new LinearShockSensorConfiguration
            {
                Length = 67.25,
                Resolution = 16
            })
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var length = view.FindControl<NumericUpDown>("LengthNumericUpDown");
            Assert.NotNull(length);
            Assert.Equal(67.25, Convert.ToDouble(length!.Value));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task LinearShockSensorConfigurationView_ResolutionNumericUpDown_DisplaysBoundValue()
    {
        var view = new LinearShockSensorConfigurationView
        {
            DataContext = new LinearShockSensorConfigurationViewModel(new LinearShockSensorConfiguration
            {
                Length = 67.25,
                Resolution = 16
            })
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var resolution = view.FindControl<NumericUpDown>("ResolutionNumericUpDown");
            Assert.NotNull(resolution);
            Assert.Equal(16, Convert.ToInt32(resolution!.Value));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}