using MessagePack;

#pragma warning disable CS8618

namespace Sufni.Telemetry;

[MessagePackObject(keyAsPropertyName: true)]
public class Metadata
{
    public string SourceName { get; set; }
    public int Version { get; set; }
    public int SampleRate { get; set; }
    public long Timestamp { get; set; }
    public double Duration { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class Airtime
{
    public double Start { get; set; }
    public double End { get; set; }
}

[MessagePackObject(keyAsPropertyName: true)]
public class Suspension
{
    public bool Present { get; set; }
    public double AnomalyRate { get; set; }
    public double? MaxTravel { get; set; }
    public double[] Travel { get; set; }
    public double[] Velocity { get; set; }
    public Strokes Strokes { get; set; }
    public double[] TravelBins { get; set; }
    public double[] VelocityBins { get; set; }
    public double[] FineVelocityBins { get; set; }
}

public record BikeData(
    double HeadAngle,
    double? FrontMaxTravel,
    double? RearMaxTravel,
    Func<ushort, double>? FrontMeasurementToTravel,
    Func<ushort, double>? RearMeasurementToTravel,
    bool FrontMeasurementWraps = false,
    bool RearMeasurementWraps = false);

public record HistogramData(List<double> Bins, List<double> Values);

public record StackedHistogramData(List<double> Bins, List<double[]> Values);

public record TravelStatistics(double Max, double Average, int Bottomouts);

public record VelocityStatistics(
    double AverageRebound,
    double MaxRebound,
    double AverageCompression,
    double MaxCompression,
    double Percentile95Rebound = 0,
    double Percentile95Compression = 0,
    int ReboundStrokeCount = 0,
    int CompressionStrokeCount = 0);

public record NormalDistributionData(
    List<double> Y,
    List<double> Pdf);

public record VelocityBands(
    double LowSpeedCompression,
    double HighSpeedCompression,
    double LowSpeedRebound,
    double HighSpeedRebound);

public enum SuspensionType
{
    Front,
    Rear,
}

public enum BalanceType
{
    Compression,
    Rebound,
}

public enum ImuLocation
{
    Frame = 0,
    Fork = 1,
    Shock = 2,
}

public record BalanceData(
    List<double> FrontTravel,
    List<double> FrontVelocity,
    List<double> FrontTrend,
    List<double> RearTravel,
    List<double> RearVelocity,
    List<double> RearTrend,
    double MeanSignedDeviation,
    double FrontSlope = 0,
    double RearSlope = 0,
    double SignedSlopeDeltaPercent = 0,
    double AbsoluteSlopeDeltaPercent = 0);

public record StrokeThirds(double Lower, double Middle, double Upper);

public record VibrationStats(
    double CompressionPercent,
    double ReboundPercent,
    double OtherPercent,
    double MagicCarpet,
    double AverageGCompression,
    double AverageGRebound,
    double AverageGOverall,
    StrokeThirds CompressionThirds,
    StrokeThirds ReboundThirds,
    StrokeThirds OverallThirds);
