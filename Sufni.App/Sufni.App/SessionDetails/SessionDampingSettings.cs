namespace Sufni.App.SessionDetails;

public static class SessionDampingSettings
{
    public const double HighSpeedThresholdMmPerSecond = DampingSpeedCutoffs.DefaultMmPerSecond;
    public const double VelocityHistogramLimitMmPerSecond = DampingSpeedCutoffs.MaximumMmPerSecond;
}
