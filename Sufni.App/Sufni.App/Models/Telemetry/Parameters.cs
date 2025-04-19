namespace Sufni.App.Models.Telemetry;

public static class Parameters
{
    // (s) minimum duration to consider stroke an idle period
    public const double IdlingDurationThreshold = 0.10;

    // (s) minimum duration to consider stroke an airtime
    public const double AirtimeDurationThreshold = 0.20;

    // (mm/s) minimum velocity after stroke to consider it an airtime
    public const double AirtimeVelocityThreshold = 500;

    // f&r airtime candidates must overlap at least this amount to be an airtime
    public const double AirtimeOverlapThreshold = 0.5;

    // stroke f&r mean travel must be below max*this to be an airtime
    public const double AirtimeTravelMeanThresholdRatio = 0.04;

    // (mm) minimum length to consider stroke a compression/rebound
    public const double StrokeLengthThreshold = 5;

    // (mm/s) step between velocity histogram bins
    public const double VelocityHistStep = 100.0;

    // (mm/s) step between fine-grained velocity histogram bins
    public const double VelocityHistStepFine = 15.0;

    // (mm) bottom-outs are regions where travel > max_travel - this value
    public const double BottomoutThreshold = 3;

    // number of travel histogram bins
    public const int TravelHistBins = 20;
}