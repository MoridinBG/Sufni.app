using System;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SessionPages;
using Sufni.App.Views.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.SessionPages;

public class NotesPageViewTests
{
    [AvaloniaFact]
    public async Task NotesPageView_BindsEditableFieldsFromViewModel()
    {
        var viewModel = new NotesPageViewModel
        {
            Description = "Initial notes",
        };
        viewModel.ForkSettings.SpringRate = "58 psi";
        viewModel.ShockSettings.SpringRate = "182 psi";
        viewModel.ForkSettings.HighSpeedCompression = 3;
        viewModel.ShockSettings.HighSpeedCompression = 6;
        viewModel.ForkSettings.HighSpeedRebound = 4;
        viewModel.ShockSettings.HighSpeedRebound = 7;

        await using var mounted = await MountAsync(viewModel);

        var forkSpringRate = mounted.View.FindControl<TextBox>("ForkSpringRateTextBox");
        var shockSpringRate = mounted.View.FindControl<TextBox>("ShockSpringRateTextBox");
        var forkHsc = mounted.View.FindControl<NumericUpDown>("ForkHscInput");
        var shockHsc = mounted.View.FindControl<NumericUpDown>("ShockHscInput");
        var forkHsr = mounted.View.FindControl<NumericUpDown>("ForkHsrInput");
        var shockHsr = mounted.View.FindControl<NumericUpDown>("ShockHsrInput");
        var description = mounted.View.FindControl<TextBox>("DescriptionTextBox");

        Assert.NotNull(forkSpringRate);
        Assert.NotNull(shockSpringRate);
        Assert.NotNull(forkHsc);
        Assert.NotNull(shockHsc);
        Assert.NotNull(forkHsr);
        Assert.NotNull(shockHsr);
        Assert.NotNull(description);
        Assert.Equal("58 psi", forkSpringRate!.Text);
        Assert.Equal("182 psi", shockSpringRate!.Text);
        Assert.Equal(3, Convert.ToInt32(forkHsc!.Value));
        Assert.Equal(6, Convert.ToInt32(shockHsc!.Value));
        Assert.Equal(4, Convert.ToInt32(forkHsr!.Value));
        Assert.Equal(7, Convert.ToInt32(shockHsr!.Value));
        Assert.Equal("Initial notes", description!.Text);

        viewModel.ForkSettings.SpringRate = "60 psi";
        viewModel.ShockSettings.SpringRate = "185 psi";
        viewModel.ForkSettings.HighSpeedCompression = 5;
        viewModel.ShockSettings.HighSpeedCompression = 8;
        viewModel.ForkSettings.HighSpeedRebound = 6;
        viewModel.ShockSettings.HighSpeedRebound = 9;
        viewModel.Description = "Updated notes";
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("60 psi", forkSpringRate.Text);
        Assert.Equal("185 psi", shockSpringRate.Text);
        Assert.Equal(5, Convert.ToInt32(forkHsc.Value));
        Assert.Equal(8, Convert.ToInt32(shockHsc.Value));
        Assert.Equal(6, Convert.ToInt32(forkHsr.Value));
        Assert.Equal(9, Convert.ToInt32(shockHsr.Value));
        Assert.Equal("Updated notes", description.Text);
    }

    [AvaloniaFact]
    public async Task NotesPageView_UpdatesViewModel_WhenEditableFieldsChange()
    {
        var viewModel = new NotesPageViewModel();

        await using var mounted = await MountAsync(viewModel);

        var forkSpringRate = mounted.View.FindControl<TextBox>("ForkSpringRateTextBox");
        var shockSpringRate = mounted.View.FindControl<TextBox>("ShockSpringRateTextBox");
        var forkHsc = mounted.View.FindControl<NumericUpDown>("ForkHscInput");
        var shockHsr = mounted.View.FindControl<NumericUpDown>("ShockHsrInput");
        var description = mounted.View.FindControl<TextBox>("DescriptionTextBox");

        Assert.NotNull(forkSpringRate);
        Assert.NotNull(shockSpringRate);
        Assert.NotNull(forkHsc);
        Assert.NotNull(shockHsr);
        Assert.NotNull(description);

        forkSpringRate!.Text = "62 psi";
        shockSpringRate!.Text = "188 psi";
        forkHsc!.Value = 4;
        shockHsr!.Value = 10;
        description!.Text = "Ride notes";
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal("62 psi", viewModel.ForkSettings.SpringRate);
        Assert.Equal("188 psi", viewModel.ShockSettings.SpringRate);
        Assert.Equal((uint)4, viewModel.ForkSettings.HighSpeedCompression);
        Assert.Equal((uint)10, viewModel.ShockSettings.HighSpeedRebound);
        Assert.Equal("Ride notes", viewModel.Description);
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
        var itemsControl = mounted.View.FindControl<ItemsControl>("TemperatureAveragesItemsControl");

        Assert.NotNull(panel);
        Assert.NotNull(itemsControl);
        Assert.True(panel!.IsVisible);
        var rows = itemsControl!.Items.Cast<TemperatureAverageRowViewModel>().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal("Frame", rows[0].SensorName);
        Assert.Equal($"{18.26.ToString("F1", CultureInfo.CurrentCulture)} C", rows[0].TemperatureText);
        Assert.Equal("Fork", rows[1].SensorName);
        Assert.Equal($"{21.76.ToString("F1", CultureInfo.CurrentCulture)} C", rows[1].TemperatureText);

        viewModel.SetTemperatureAverages([]);
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.False(panel.IsVisible);
    }

    private static async Task<MountedNotesPageView> MountAsync(NotesPageViewModel viewModel)
    {
        ViewTestHelpers.EnsureViewTestResources();

        var view = new NotesPageView
        {
            DataContext = viewModel,
        };

        var host = await ViewTestHelpers.ShowViewAsync(new ScrollViewer { Content = view });
        return new MountedNotesPageView(host, view);
    }
}

internal sealed class MountedNotesPageView(Window host, NotesPageView view) : IAsyncDisposable
{
    public Window Host { get; } = host;
    public NotesPageView View { get; } = view;

    public async ValueTask DisposeAsync()
    {
        Host.Close();
        await ViewTestHelpers.FlushDispatcherAsync();
    }
}
