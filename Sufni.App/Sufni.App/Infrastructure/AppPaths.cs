using System;
using System.Globalization;
using System.IO;

namespace Sufni.App.Infrastructure;

public interface IAppPaths
{
    string AppDataDirectory { get; }
    string DatabasePath { get; }
    string PreferencesPath { get; }
    string LogsDirectory { get; }
    string CertificatePath { get; }
    string CreateSessionLogPath(DateTimeOffset startupTime);
}

public sealed class AppPaths(string appDataDirectory) : IAppPaths
{
    public string AppDataDirectory { get; } = appDataDirectory;
    public string DatabasePath => Path.Combine(AppDataDirectory, "sst.db");
    public string PreferencesPath => Path.Combine(AppDataDirectory, "app-preferences.json");
    public string LogsDirectory => Path.Combine(AppDataDirectory, "logs");
    public string CertificatePath => Path.Combine(AppDataDirectory, "certificate.pfx");

    public string CreateSessionLogPath(DateTimeOffset startupTime) =>
        Path.Combine(
            LogsDirectory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"LOG-{startupTime:yyyyMMdd-HHmmss}.log"));
}
