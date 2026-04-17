using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using AppleOSLog = CoreFoundation.OSLog;
using AppleLogLevel = CoreFoundation.OSLogLevel;

namespace Sufni.App.iOS;

internal sealed class OsLogSink : ILogEventSink
{
    private readonly AppleOSLog log;
    private readonly MessageTemplateTextFormatter formatter;

    public OsLogSink(string outputTemplate, string subsystem = "app.sufni", string category = "default")
    {
        log = new AppleOSLog(subsystem, category);
        formatter = new MessageTemplateTextFormatter(outputTemplate);
    }

    public void Emit(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        formatter.Format(logEvent, writer);
        log.Log(MapLevel(logEvent.Level), writer.ToString().TrimEnd());
    }

    private static AppleLogLevel MapLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => AppleLogLevel.Debug,
        LogEventLevel.Debug => AppleLogLevel.Debug,
        LogEventLevel.Information => AppleLogLevel.Info,
        LogEventLevel.Warning => AppleLogLevel.Default,
        LogEventLevel.Error => AppleLogLevel.Error,
        LogEventLevel.Fatal => AppleLogLevel.Fault,
        _ => AppleLogLevel.Default,
    };
}
