using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;
using MathNet.Numerics;
using MessagePack;
using SQLite;

namespace Sufni.App.Models.Telemetry;

[Table("linkage")]
[MessagePackObject(keyAsPropertyName: true)]
public class Linkage : Synchronizable
{
    private LeverageRatioData? leverageRatioData;
    private double? maxFrontTravel;
    private double? maxRearTravel;
    private double[]? shockWheelCoeffs;

#pragma warning disable IDE0052 // Remove unread private members
                                // leverageRatio is used by the MessagePack deserializer
    private double[][]? leverageRatio;
#pragma warning restore IDE0052 // Remove unread private members

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Linkage() { }

    public Linkage(Guid id, string name, double headAngle, double? maxFrontStroke, double? maxRearStroke, string? rawData)
    {
        Id = id;
        Name = name;
        HeadAngle = headAngle;
        MaxFrontStroke = maxFrontStroke;
        MaxRearStroke = maxRearStroke;
        RawData = rawData;
    }

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    [IgnoreMember]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("head_angle")]
    [Column("head_angle")]
    public double HeadAngle { get; set; }

    [JsonPropertyName("front_stroke")]
    [Column("front_stroke")]
    public double? MaxFrontStroke { get; set; }

    [JsonPropertyName("rear_stroke")]
    [Column("rear_stroke")]
    public double? MaxRearStroke { get; set; }

    [JsonPropertyName("data")]
    [Column("raw_lr_data")]
    [IgnoreMember]
    public string? RawData { get; set; }

    [Ignore]
    [JsonIgnore]
    public double MaxFrontTravel
    {
        get
        {
            maxFrontTravel ??= Math.Sin(HeadAngle * Math.PI / 180.0) * MaxFrontStroke ?? 0;
            return maxFrontTravel.Value;
        }
        set => maxFrontTravel = value;
    }

    [Ignore]
    [JsonIgnore]
    public double MaxRearTravel
    {
        get
        {
            maxRearTravel ??= Polynomial.Evaluate(MaxRearStroke ?? 0);
            return maxRearTravel.Value;
        }
        set => maxRearTravel = value;
    }

    [Ignore]
    [JsonIgnore]
    public double[] ShockWheelCoeffs
    {
        get
        {
            shockWheelCoeffs ??= Fit.Polynomial(LeverageRatioData?.ShockTravel.ToArray(),
                LeverageRatioData?.WheelTravel.ToArray(), 3);
            return shockWheelCoeffs;

        }
        set => shockWheelCoeffs = value;
    }

    [Ignore][JsonIgnore][IgnoreMember] public Polynomial Polynomial => new(ShockWheelCoeffs);

    [Ignore]
    [JsonIgnore]
    public double[][]? LeverageRatio
    {
        get => LeverageRatioData?.ToArray();
        set => leverageRatio = value;
    }

    [Ignore]
    [JsonIgnore]
    [IgnoreMember]
    public LeverageRatioData? LeverageRatioData
    {
        get
        {
            if (leverageRatioData is null && RawData is not null)
            {
                // Linkage.data does not have a header, it's just "wt,lr" pairs per line,
                // so we add the header so that LeverageFromCsv can process it.
                var csv = $"Wheel_T,Leverage_R\n{RawData}";
                leverageRatioData = new LeverageRatioData(new StringReader(csv));
            }

            return leverageRatioData;
        }
    }
}

public class LeverageRatioData
{
    public List<double> WheelTravel { get; init; }
    public List<double> LeverageRatio { get; init; }
    public List<double> ShockTravel { get; init; }

    private void ProcessWheelLeverageRatio(CsvReader reader)
    {
        var shock = 0.0;
        while (reader.Read())
        {
            var wheel = reader.GetField<double>("Wheel_T");
            var leverage = reader.GetField<double>("Leverage_R");

            WheelTravel.Add(wheel);
            LeverageRatio.Add(leverage);
            ShockTravel.Add(shock);
            shock += 1.0 / leverage;
        }
    }

    private void ProcessWheelTravelShockTravel(CsvReader reader)
    {
        var idx = 0;

        while (reader.Read())
        {
            var shock = reader.GetField<double>("Shock_T");
            var wheel = reader.GetField<double>("Wheel_T");
            double lr = 0;

            if (idx > 0)
            {
                var sdiff = shock - ShockTravel[idx - 1];
                var wdiff = wheel - WheelTravel[idx - 1];
                lr = wdiff / sdiff;
                LeverageRatio[idx - 1] = lr;
            }

            ShockTravel.Add(shock);
            WheelTravel.Add(wheel);
            LeverageRatio.Add(lr);

            idx++;
        }
    }

    public LeverageRatioData(TextReader reader)
    {
        WheelTravel = [];
        LeverageRatio = [];
        ShockTravel = [];

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            DetectDelimiter = true,
            AllowComments = true,
            Comment = '#'
        };

        using var csvReader = new CsvReader(reader, config);

        if (!csvReader.Read() || !csvReader.ReadHeader() || !csvReader.HeaderRecord!.Contains("Wheel_T"))
        {
            throw new Exception("Failed processing leverage data.");
        }

        if (csvReader.HeaderRecord!.Contains("Leverage_R"))
        {
            ProcessWheelLeverageRatio(csvReader);
        }

        if (csvReader.HeaderRecord!.Contains("Shock_T"))
        {
            ProcessWheelTravelShockTravel(csvReader);
        }
    }

    public LeverageRatioData(double[][] data)
    {
        WheelTravel = new List<double>(data.Length);
        LeverageRatio = new List<double>(data.Length);
        ShockTravel = new List<double>(data.Length);

        foreach (var d in data)
        {
            WheelTravel.Add(d[0]);
            LeverageRatio.Add(d[1]);
        }
    }

    public double[][] ToArray()
    {
        var data = new double[WheelTravel.Count][];
        for (var i = 0; i < WheelTravel.Count; i++)
        {
            data[i] = [WheelTravel[i], LeverageRatio[i]];
        }

        return data;
    }

    public override string ToString()
    {
        return string.Join("\n", WheelTravel.Zip(LeverageRatio, (wt, lr) =>
            string.Create(CultureInfo.InvariantCulture, $"{wt},{lr}")));
    }

    public override int GetHashCode()
    {
        return LeverageRatio.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        if (obj is LeverageRatioData other)
        {
            return WheelTravel.SequenceEqual(other.WheelTravel) &&
                   LeverageRatio.SequenceEqual(other.LeverageRatio);
        }

        return false;
    }
}