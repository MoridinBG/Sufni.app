using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace Sufni.App.Infrastructure;

public static class LoggingBootstrapper
{
    public const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    private static readonly object gate = new();
    private static bool isInitialized;
    private static bool hooksInstalled;
    private static string? currentPlatformName;
    private static ILogEventSink? currentPlatformSink;

    public static string? CurrentLogFilePath { get; private set; }

    public static void Initialize(string platformName, ILogEventSink? platformSink = null)
    {
        lock (gate)
        {
            if (isInitialized)
            {
                return;
            }

            try
            {
                currentPlatformName = platformName;
                currentPlatformSink = platformSink;
                AppPaths.CreateRequiredDirectories();
                CurrentLogFilePath ??= AppPaths.CreateSessionLogPath(DateTimeOffset.Now);

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
            if (!isInitialized || currentPlatformName is null || CurrentLogFilePath is null)
            {
                return;
            }

            try
            {
                var logger = Log.Logger;
                Log.Logger = new LoggerConfiguration().CreateLogger();
                (logger as IDisposable)?.Dispose();
                Log.Logger = CreateLogger(currentPlatformName, CurrentLogFilePath, currentPlatformSink);
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
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.WithProperty("Platform", platformName)
            .Enrich.WithProperty("ProcessId", Environment.ProcessId);

#if DEBUG
        configuration = configuration
            .WriteTo.File(logFilePath, outputTemplate: OutputTemplate)
            .WriteTo.Debug(outputTemplate: OutputTemplate);
#else
        configuration = configuration
            .WriteTo.Logger(lc => lc
                .Filter.ByExcluding(logEvent => logEvent.Level == LogEventLevel.Debug)
                .WriteTo.File(logFilePath, outputTemplate: OutputTemplate));
#endif

        if (platformSink is not null)
        {
            configuration = configuration.WriteTo.Sink(platformSink);
        }

        return configuration.CreateLogger();
    }
}
