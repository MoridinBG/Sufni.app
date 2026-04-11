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
    public async Task LinearShockSensorConfigurationView_LoadsConfiguredValues()
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
            var resolution = view.FindControl<NumericUpDown>("ResolutionNumericUpDown");
            Assert.NotNull(length);
            Assert.NotNull(resolution);

            Assert.Equal(67.25, Convert.ToDouble(length!.Value));
            Assert.Equal(16, Convert.ToInt32(resolution!.Value));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}