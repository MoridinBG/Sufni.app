using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Adletec.Sonic;
using MessagePack;
using MessagePack.Formatters;
using SQLite;

namespace Sufni.App.Models.Telemetry;

public class IdFormatter : IMessagePackFormatter<Guid>
{
    public Guid Deserialize(ref MessagePackReader reader, MessagePackSerializerOptions options)
    {
        // Need to check type first, because CLR/MessagePack.MessagePackSerializationExceptions
        // are not caught by the catch block
        if (reader.NextMessagePackType == MessagePackType.Integer)
        {
            Int32Formatter.Instance.Deserialize(ref reader, options);
            return Guid.Empty;
        }
        try
        {
            return GuidFormatter.Instance.Deserialize(ref reader, options);
        }
        catch (Exception)
        {
            return Guid.Empty;
        }
    }

    public void Serialize(ref MessagePackWriter writer, Guid value, MessagePackSerializerOptions options)
    {
        GuidFormatter.Instance.Serialize(ref writer, value, options);
    }
}

[Table("calibration")]
[MessagePackObject(keyAsPropertyName: true)]
public partial class Calibration : Synchronizable
{
    private Func<Dictionary<string, double>, double>? evaluatorDelegate;
    private readonly Dictionary<string, double> evaluatorEnvironment = [];

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacesRegex();

    // Just to satisfy sql-net-pcl's parameterless constructor requirement
    // Uninitialized non-nullable property warnings are suppressed with null! initializer.
    public Calibration() { }

    public Calibration(Guid id, string name, Guid methodId, Dictionary<string, double> inputs)
    {
        Id = id;
        Name = name;
        MethodId = methodId;
        Inputs = inputs;
    }

    [JsonPropertyName("id")]
    [PrimaryKey]
    [Column("id")]
    [IgnoreMember]
    public Guid Id { get; set; } = Guid.NewGuid();

    [JsonPropertyName("name")]
    [Column("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("method_id")]
    [Column("method_id")]
    [MessagePackFormatter(typeof(IdFormatter))]
    public Guid MethodId { get; set; }

    [JsonPropertyName("inputs")]
    [Ignore]
    public Dictionary<string, double> Inputs { get; set; } = null!;

    [JsonIgnore]
    [Column("inputs")]
    [IgnoreMember]
    public string? InputsRaw
    {
        get => JsonSerializer.Serialize(Inputs);
        set
        {
            Debug.Assert(value != null, nameof(value) + " != null");
            Inputs = JsonSerializer.Deserialize<Dictionary<string, double>>(value) ??
                     throw new InvalidOperationException();
        }
    }

    public void Prepare(CalibrationMethod method, double maxStroke, double maxTravel)
    {
        var expression = WhitespacesRegex().Replace(method.Properties.Expression, "");
        var evaluator = Evaluator.Create()
            .UseCulture(CultureInfo.InvariantCulture)
            .AddConstant("MAX_STROKE", maxStroke)
            .AddConstant("MAX_TRAVEL", maxTravel)
            .Build();
        evaluatorDelegate = evaluator.CreateDelegate(expression);

        // Set calibration variables (a.k.a. inputs)
        foreach (var input in Inputs)
        {
            evaluatorEnvironment[input.Key] = input.Value;
        }

        // Calculate intermediates
        foreach (var intermediate in method.Properties.Intermediates)
        {
            var exp = WhitespacesRegex().Replace(intermediate.Value, "");
            evaluatorEnvironment[intermediate.Key] = evaluator.Evaluate(exp, evaluatorEnvironment);
        }
    }

    public double Evaluate(double sample)
    {
        if (evaluatorDelegate is null)
        {
            return double.NaN;
        }

        evaluatorEnvironment["sample"] = sample;

        try
        {
            return evaluatorDelegate(evaluatorEnvironment);
        }
        catch (Exception)
        {
            return double.NaN;
        }
    }
}
