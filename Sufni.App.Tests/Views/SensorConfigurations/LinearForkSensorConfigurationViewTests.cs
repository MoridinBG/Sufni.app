using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SensorConfigurations;
using Sufni.App.Views.SensorConfigurations;

namespace Sufni.App.Tests.Views.SensorConfigurations;

public class LinearForkSensorConfigurationViewTests
{
    [AvaloniaFact]
    public async Task LinearForkSensorConfigurationView_LoadsConfiguredValues()
    {
        var view = new LinearForkSensorConfigurationView
        {
            DataContext = new LinearForkSensorConfigurationViewModel(new LinearForkSensorConfiguration
            {
                Length = 173.5,
                Resolution = 12
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

            Assert.Equal(173.5, Convert.ToDouble(length!.Value));
            Assert.Equal(12, Convert.ToInt32(resolution!.Value));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}