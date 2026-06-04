namespace Sufni.Telemetry;

public abstract record TelemetryRangeSelection(SuspensionType SuspensionType)
{
    public readonly record struct BinRange(
        int Index,
        double Start,
        double End,
        bool IsFirst,
        bool IsLast)
    {
        public static BinRange FromBins(IReadOnlyList<double> bins, int index)
        {
            if (bins.Count < 2 || index < 0 || index >= bins.Count - 1)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new BinRange(
                index,
                bins[index],
                bins[index + 1],
                index == 0,
                index == bins.Count - 2);
        }

        public bool MatchesBins(IReadOnlyList<double> bins, double tolerance = 1e-9)
        {
            return bins.Count >= 2 &&
                   Index >= 0 &&
                   Index < bins.Count - 1 &&
                   Math.Abs(Start - bins[Index]) <= tolerance &&
                   Math.Abs(End - bins[Index + 1]) <= tolerance &&
                   IsFirst == (Index == 0) &&
                   IsLast == (Index == bins.Count - 2);
        }
    }
}

public sealed record DampingRangeSelection(
    SuspensionType SuspensionType,
    VelocityAverageMode AverageMode,
    int VelocityBinIndex,
    int TravelBinStartIndex,
    int TravelBinEndIndex) : TelemetryRangeSelection(SuspensionType);

public sealed record StrokeLengthRangeSelection(
    SuspensionType SuspensionType,
    BalanceType StrokeKind,
    TelemetryRangeSelection.BinRange Bin) : TelemetryRangeSelection(SuspensionType);

public sealed record StrokeSpeedRangeSelection(
    SuspensionType SuspensionType,
    BalanceType StrokeKind,
    TelemetryRangeSelection.BinRange Bin) : TelemetryRangeSelection(SuspensionType);

public sealed record DeepTravelRangeSelection(
    SuspensionType SuspensionType,
    TelemetryRangeSelection.BinRange Bin) : TelemetryRangeSelection(SuspensionType);

public readonly record struct TelemetryHighlightRange(
    double StartSeconds,
    double EndSeconds,
    SuspensionType? SuspensionType = null);
