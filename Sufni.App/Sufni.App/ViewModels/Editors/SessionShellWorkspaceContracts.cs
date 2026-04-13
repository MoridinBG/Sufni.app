using CommunityToolkit.Mvvm.Input;
using Sufni.App.SessionDetails;
using Sufni.App.ViewModels.SessionPages;
using Sufni.Telemetry;

namespace Sufni.App.ViewModels.Editors;

public interface IRecordedSessionGraphWorkspace
{
    TelemetryData? TelemetryData { get; }
    SessionTimelineLinkViewModel Timeline { get; }
}

public interface ISessionMediaWorkspace
{
    MapViewModel? MapViewModel { get; }
    SessionTimelineLinkViewModel Timeline { get; }
    double? MapVideoWidth { get; }
    string? VideoUrl { get; }
}

public interface ISessionStatisticsWorkspace
{
    TelemetryData? TelemetryData { get; }
    SessionDamperPercentages DamperPercentages { get; }
}

public interface ISessionSidebarWorkspace
{
    string? Name { get; set; }
    string? DescriptionText { get; set; }
    SuspensionSettings ForkSettings { get; }
    SuspensionSettings ShockSettings { get; }
    IAsyncRelayCommand SaveCommand { get; }
    IAsyncRelayCommand ResetCommand { get; }
}