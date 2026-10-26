using Sufni.App.Bikes.Models;
namespace Sufni.App.Sessions.Processing.SessionDetails;

public static class SessionDampingSettings
{
    public const double HighSpeedThresholdMmPerSecond = DampingSpeedCutoffs.DefaultMmPerSecond;
    public const double VelocityDistributionLimitMmPerSecond = DampingSpeedCutoffs.MaximumMmPerSecond;
}
