using System;
using System.Collections.Generic;
using Avalonia.Logging;
using Serilog;
using AvaloniaLevel = Avalonia.Logging.LogEventLevel;

namespace Sufni.App.Services;

public sealed class AvaloniaSerilogSink(AvaloniaLevel minimumLevel = AvaloniaLevel.Warning, params string[] areas) : ILogSink
{
    private readonly HashSet<string>? enabledAreas = areas.Length == 0
        ? null
        : new HashSet<string>(areas, StringComparer.OrdinalIgnoreCase);

    public bool IsEnabled(AvaloniaLevel level, string area) =>
        level >= minimumLevel &&
        (enabledAreas is null || enabledAreas.Contains(area));

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate)
    {
        Write(level, area, source, messageTemplate, []);
    }

    public void Log(AvaloniaLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues)
    {
        Write(level, area, source, messageTemplate, propertyValues);
    }

    private static Serilog.Events.LogEventLevel MapLevel(AvaloniaLevel level) => level switch
    {
        AvaloniaLevel.Verbose => Serilog.Events.LogEventLevel.Verbose,
        AvaloniaLevel.Debug => Serilog.Events.LogEventLevel.Debug,
        AvaloniaLevel.Information => Serilog.Events.LogEventLevel.Information,
        AvaloniaLevel.Warning => Serilog.Events.LogEventLevel.Warning,
        AvaloniaLevel.Error => Serilog.Events.LogEventLevel.Error,
        AvaloniaLevel.Fatal => Serilog.Events.LogEventLevel.Fatal,
        _ => Serilog.Events.LogEventLevel.Information,
    };

    private static void Write(AvaloniaLevel level, string area, object? source, string messageTemplate, object?[] propertyValues)
    {
        var logger = Serilog.Log.ForContext("AvaloniaArea", area);
        if (source is not null)
        {
            logger = logger.ForContext("AvaloniaSource", source.ToString());
        }

        logger.Write(MapLevel(level), messageTemplate, propertyValues);
    }
}