using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Sufni.Telemetry.Tests;

public class TelemetryNormalDistributionTests
{
    [Fact]
    public void CalculateNormalDistribution_PreservesExactFullRangeFingerprint()
    {
        var telemetry = CreateTelemetry();

        var result = TelemetryStatistics.CalculateNormalDistribution(telemetry, SuspensionType.Front);

        Assert.Equal("014c4e5eb453c19588951dde05ab5346043f59213e07f6793c9efb3f63566909", Fingerprint(result));
    }

    [Fact]
    public void CalculateNormalDistribution_PreservesExactWholeStrokeRangeFingerprint()
    {
        var telemetry = CreateTelemetry();

        var result = TelemetryStatistics.CalculateNormalDistribution(
            telemetry,
            SuspensionType.Front,
            new TelemetryTimeRange(0.1, 0.65));

        Assert.Equal("f83447f1c7a597d958d4c3306fdfdcde0ec56e854a5d0dc07ce3edfd835a9d18", Fingerprint(result));
    }

    private static TelemetryData CreateTelemetry()
    {
        var velocity = new[] { 900.0, -3.25, 0.5, 2.75, 800.0, 700.0, -4.5, -1.25, 3.5, 6.25 };
        var front = new Suspension
        {
            Present = true,
            Travel = new double[velocity.Length],
            Velocity = velocity,
            TravelBins = [0, 100],
            VelocityBins = [-100, 0, 100],
            FineVelocityBins = [-100, 0, 100],
            Strokes = new Strokes
            {
                Compressions = [new Stroke { Start = 1, End = 3 }],
                Rebounds = [new Stroke { Start = 6, End = 9 }],
            },
        };
        return new TelemetryData
        {
            Metadata = new Metadata { SampleRate = 10, Duration = 1 },
            Front = front,
            Rear = new Suspension
            {
                Travel = [],
                Velocity = [],
                TravelBins = [],
                VelocityBins = [],
                FineVelocityBins = [],
                Strokes = new Strokes { Compressions = [], Rebounds = [] },
            },
            Airtimes = [],
            Markers = [],
        };
    }

    private static string Fingerprint(NormalDistributionData value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, value.Y.Count);
        foreach (var sample in value.Y)
        {
            Append(hash, BitConverter.DoubleToInt64Bits(sample));
        }
        Append(hash, value.Pdf.Count);
        foreach (var sample in value.Pdf)
        {
            Append(hash, BitConverter.DoubleToInt64Bits(sample));
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static void Append(IncrementalHash hash, long value)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
        hash.AppendData(buffer);
    }
}
