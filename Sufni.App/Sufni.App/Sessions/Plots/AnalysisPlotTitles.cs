using Sufni.Telemetry;

namespace Sufni.App.Sessions.Plots;

internal static class AnalysisPlotTitles
{
    public static string TravelDistribution(SuspensionType type, TravelDistributionMode mode)
        => $"{SuspensionName(type)} travel distribution";

    public static string TravelFrequencyDistribution(SuspensionType type) =>
        $"{SuspensionName(type)} travel frequencies";

    public static string VelocityDistribution(SuspensionType type, VelocityAverageMode mode)
        => $"{SuspensionName(type)} suspension velocity distribution";

    public static string Balance(BalanceType type, BalanceDisplacementMode displacementMode, BalanceSpeedMode speedMode)
        => type == BalanceType.Compression ? "Compression balance" : "Rebound balance";

    public static string StrokeLengthDistribution(SuspensionType type, BalanceType strokeKind) =>
        $"{SuspensionName(type)} {StrokeName(strokeKind)} stroke length";

    public static string StrokeSpeedDistribution(SuspensionType type, BalanceType strokeKind) =>
        $"{SuspensionName(type)} {StrokeName(strokeKind)} stroke speed";

    public static string DeepTravelDistribution(SuspensionType type) =>
        $"{SuspensionName(type)} deep-travel strokes";

    public static string VibrationDistribution(SuspensionType type, ImuLocation location) =>
        $"{SuspensionName(type)} {location.ToString().ToLowerInvariant()} vibration distribution";

    private static string SuspensionName(SuspensionType type) =>
        type == SuspensionType.Front ? "Front" : "Rear";

    private static string StrokeName(BalanceType strokeKind) =>
        strokeKind == BalanceType.Compression ? "compression" : "rebound";
}
