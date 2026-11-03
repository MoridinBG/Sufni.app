using System;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.Telemetry;

using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
using Sufni.App.Sessions.Pages.Views.SessionPages;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Sessions.Pages.Views.SessionPages;

[Collection("Ui")]
public class NotesPageViewTests
{
    [AvaloniaFact]
    public async Task NotesPageView_BindsEditableFieldsFromViewModel()
    {
        var viewModel = new NotesPageViewModel { Description = "Initial notes" };
        viewModel.ShockSettings.HighSpeedCompression = 6;
        await using var mounted = await MountAsync(viewModel);
        var description = mounted.View.FindControl<TextBox>("DescriptionTextBox")!;
        var shockHsc = mounted.View.FindControl<NumericUpDown>("ShockHscInput")!;

        Assert.Equal("Initial notes", description.Text);
        Assert.Equal(6, Convert.ToInt32(shockHsc.Value));

        description.Text = "Ride notes";
        shockHsc.Value = 8;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("Ride notes", viewModel.Description);
        Assert.Equal((uint)8, viewModel.ShockSettings.HighSpeedCompression);
    }

    [AvaloniaFact]
    public async Task NotesPageView_ShowsTemperatureAverages_WhenPresent()
    {
        var viewModel = new NotesPageViewModel();
        viewModel.SetTemperatureAverages(
        [
            new TemperatureAverage(0, 18.26),
            new TemperatureAverage(1, 21.76)
        ]);

        await using var mounted = await MountAsync(viewModel);

        var panel = mounted.View.FindControl<StackPanel>("TemperatureAveragesPanel");

        Assert.NotNull(panel);
        Assert.True(panel!.IsVisible);

        viewModel.SetTemperatureAverages([]);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(panel.IsVisible);
    }

    private static async Task<MountedView<NotesPageView>> MountAsync(NotesPageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new NotesPageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedView<NotesPageView>(host, view);
    }
}
