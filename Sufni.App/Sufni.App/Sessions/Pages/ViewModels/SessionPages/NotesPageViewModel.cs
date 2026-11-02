using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Sufni.Telemetry;

namespace Sufni.App.Sessions.Pages.ViewModels.SessionPages;

public partial class SuspensionSettings : ObservableObject
{
    [ObservableProperty] public partial string? SpringRate { get; set; }
    [ObservableProperty] public partial uint? HighSpeedCompression { get; set; }
    [ObservableProperty] public partial uint? LowSpeedCompression { get; set; }
    [ObservableProperty] public partial uint? LowSpeedRebound { get; set; }
    [ObservableProperty] public partial uint? HighSpeedRebound { get; set; }
}

public record TemperatureAverageRowViewModel(string SensorName, string TemperatureText);

public partial class NotesPageViewModel() : PageViewModelBase("Notes")
{
    [ObservableProperty] public partial string? Description { get; set; }

    public SuspensionSettings ForkSettings { get; } = new();
    public SuspensionSettings ShockSettings { get; } = new();
    public ObservableCollection<TemperatureAverageRowViewModel> TemperatureAverages { get; } = [];
    public bool HasTemperatureAverages => TemperatureAverages.Count > 0;

    public void SetTemperatureAverages(IEnumerable<TemperatureAverage> averages)
    {
        TemperatureAverages.Clear();
        foreach (var average in averages.OrderBy(average => average.LocationId))
        {
            TemperatureAverages.Add(new TemperatureAverageRowViewModel(
                SensorNameForLocation(average.LocationId),
                $"{average.TemperatureCelsius.ToString("F1", CultureInfo.CurrentCulture)} C"));
        }

        OnPropertyChanged(nameof(HasTemperatureAverages));
    }

    private static string SensorNameForLocation(byte locationId)
    {
        return locationId switch
        {
            0 => "Frame",
            1 => "Fork",
            2 => "Rear",
            _ => $"Sensor {locationId}"
        };
    }

}
