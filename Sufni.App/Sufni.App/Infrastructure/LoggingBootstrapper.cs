using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Sinks.File;

namespace Sufni.App.Infrastructure;

public static class LoggingBootstrapper
{
    public const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private static readonly System.Threading.Lock gate = new();
    private static bool isInitialized;
    private static bool hooksInstalled;
    private static FileSink? fileSink;

    internal static string? CurrentLogFilePath { get; private set; }

    public static void Initialize(string platformName, IAppPaths paths, TimeProvider timeProvider, ILogEventSink? platformSink = null)
    {
        lock (gate)
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(paths.LogsDirectory);
                CurrentLogFilePath ??= paths.CreateSessionLogPath(timeProvider.GetLocalNow());

                Log.Logger = CreateLogger(platformName, CurrentLogFilePath, platformSink);
                isInitialized = true;

                Log.Information("Starting Sufni.App on {PlatformName}", platformName);
                Log.Verbose(
                    "Startup detail: version {Version}, framework {FrameworkDescription}, os {OSDescription}, process {ProcessId}, log path {LogPath}",
                    Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown",
                    RuntimeInformation.FrameworkDescription,
                    RuntimeInformation.OSDescription,
                    Environment.ProcessId,
                    CurrentLogFilePath);
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Logging bootstrap failed: {ex}");
            }
        }
    }

    public static void Flush()
    {
        lock (gate)
        {
            if (!isInitialized)
            {
                return;
            }

            try
            {
                fileSink?.FlushToDisk();
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Logging flush failed: {ex}");
            }
        }
    }

    public static void InstallGlobalExceptionHooks()
    {
        lock (gate)
        {
            if (hooksInstalled)
            {
                return;
            }

            hooksInstalled = true;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception exception)
            {
                Log.Fatal(exception, "Unhandled application exception");
            }
            else
            {
                Log.Fatal("Unhandled application exception object: {ExceptionObject}", args.ExceptionObject);
            }

            FlushAndClose();
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Log.Error(args.Exception, "Unobserved task exception");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushAndClose();
    }

    public static void FlushAndClose()
    {
        lock (gate)
        {
            try
            {
                Log.CloseAndFlush();
                isInitialized = false;
                fileSink = null;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Logging flush failed: {ex}");
            }
        }
    }

    private static Serilog.Core.Logger CreateLogger(
        string platformName,
        string logFilePath,
        ILogEventSink? platformSink)
    {
        fileSink = new FileSink(logFilePath, new MessageTemplateTextFormatter(OutputTemplate), 1024L * 1024 * 1024);
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty("Platform", platformName)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId);

#if DEBUG
        configuration = configuration
            .WriteTo.Sink(fileSink)
            .WriteTo.Debug(outputTemplate: OutputTemplate);
#else
        configuration = configuration
            .WriteTo.Logger(lc => lc
                .Filter.ByExcluding(logEvent => logEvent.Level == LogEventLevel.Debug)
                .WriteTo.Sink(fileSink));
#endif

        if (platformSink is not null)
        {
            configuration = configuration.WriteTo.Sink(platformSink);
        }

        return configuration.CreateLogger();
    }
}
