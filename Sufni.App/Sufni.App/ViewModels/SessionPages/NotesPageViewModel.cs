using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Models;

namespace Sufni.App.ViewModels.SessionPages;

public partial class SuspensionSettings : ObservableObject
{
    [ObservableProperty] private string? springRate;
    [ObservableProperty] private uint? highSpeedCompression;
    [ObservableProperty] private uint? lowSpeedCompression;
    [ObservableProperty] private uint? lowSpeedRebound;
    [ObservableProperty] private uint? highSpeedRebound;
}

public partial class NotesPageViewModel() : PageViewModelBase("Notes")
{
    [ObservableProperty] private string? description;

    public SuspensionSettings ForkSettings { get; } = new();
    public SuspensionSettings ShockSettings { get; } = new();

    public bool IsDirty(Session session)
    {
        return
            Description != session.Description ||
            (!(ForkSettings.SpringRate is null && session.FrontSpringRate is null) && ForkSettings.SpringRate != session.FrontSpringRate) ||
            (!(ForkSettings.HighSpeedCompression is null && session.FrontHighSpeedCompression is null) && ForkSettings.HighSpeedCompression != session.FrontHighSpeedCompression) ||
            (!(ForkSettings.LowSpeedCompression is null && session.FrontLowSpeedCompression is null) && ForkSettings.LowSpeedCompression != session.FrontLowSpeedCompression) ||
            (!(ForkSettings.LowSpeedRebound is null && session.FrontLowSpeedRebound is null) && ForkSettings.LowSpeedRebound != session.FrontLowSpeedRebound) ||
            (!(ForkSettings.HighSpeedRebound is null && session.FrontHighSpeedRebound is null) && ForkSettings.HighSpeedRebound != session.FrontHighSpeedRebound) ||
            (!(ShockSettings.SpringRate is null && session.RearSpringRate is null) && ShockSettings.SpringRate != session.RearSpringRate) ||
            (!(ShockSettings.HighSpeedCompression is null && session.RearHighSpeedCompression is null) && ShockSettings.HighSpeedCompression != session.RearHighSpeedCompression) ||
            (!(ShockSettings.LowSpeedCompression is null && session.RearLowSpeedCompression is null) && ShockSettings.LowSpeedCompression != session.RearLowSpeedCompression) ||
            (!(ShockSettings.LowSpeedRebound is null && session.RearLowSpeedRebound is null) && ShockSettings.LowSpeedRebound != session.RearLowSpeedRebound) ||
            (!(ShockSettings.HighSpeedRebound is null && session.RearHighSpeedRebound is null) && ShockSettings.HighSpeedRebound != session.RearHighSpeedRebound);
    }
}
