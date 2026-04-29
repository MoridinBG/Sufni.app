namespace Sufni.App.SessionGraphs;

public static class SessionGraphSettings
{
    // Controls how often live graph plot controls flush queued stream samples to the visible plots.
    public const int LiveGraphRefreshIntervalMs = 33;

    // Controls how often the live session view projects the latest stream/control state into UI-bound properties.
    public const int LiveUiRefreshIntervalMs = 33;

    // Controls the minimum delay between full live-session statistics rebuilds from the accumulated capture.
    public const int LiveStatisticsRefreshIntervalMs = 1500;

    // Caps the display sample rate used by recorded session graphs on mobile views.
    public const int RecordedMobileMaximumDisplayHz = 100;
}