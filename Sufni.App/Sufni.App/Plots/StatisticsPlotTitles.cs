using Sufni.App.SessionDetails;
using Sufni.Telemetry;

namespace Sufni.App.Plots;

internal static class StatisticsPlotTitles
{
    public static string TravelHistogram(SuspensionType type, TravelHistogramMode mode)
        => $"{SuspensionName(type)} travel";

    public static string TravelFrequencyHistogram(SuspensionType type) =>
        $"{SuspensionName(type)} frequencies";

    public static string VelocityHistogram(SuspensionType type, VelocityAverageMode mode)
        => $"{SuspensionName(type)} velocity";

    public static string Balance(BalanceType type, BalanceDisplacementMode displacementMode, BalanceSpeedMode speedMode)
        => type == BalanceType.Compression ? "Compression balance" : "Rebound balance";

    public static string StrokeLengthHistogram(SuspensionType type, BalanceType strokeKind) =>
        $"{SuspensionName(type)} {StrokeName(strokeKind)} length";

    public static string StrokeSpeedHistogram(SuspensionType type, BalanceType strokeKind) =>
        $"{SuspensionName(type)} {StrokeName(strokeKind)} speed";

    public static string DeepTravelHistogram(SuspensionType type) =>
        $"{SuspensionName(type)} deep travel";

    public static string VibrationThirds(SuspensionType type, ImuLocation location) =>
        $"{SuspensionName(type)} {location} vibration thirds";

    private static string SuspensionName(SuspensionType type) =>
        type == SuspensionType.Front ? "Front" : "Rear";

    private static string StrokeName(BalanceType strokeKind) =>
        strokeKind == BalanceType.Compression ? "compression" : "rebound";
}
