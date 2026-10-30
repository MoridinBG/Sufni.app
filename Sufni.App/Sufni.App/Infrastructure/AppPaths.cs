using System;
using System.Globalization;
using System.IO;

namespace Sufni.App.Infrastructure;

public static class AppPaths
{
    private static readonly string appDataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sufni.App");

    public static string AppDataDirectory => appDataDirectory;

    public static string DatabasePath => Path.Combine(AppDataDirectory, "sst.db");

    public static string LogsDirectory => Path.Combine(AppDataDirectory, "logs");

    public static string CertificatePath => Path.Combine(AppDataDirectory, "certificate.pfx");

    public static void CreateRequiredDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public static string CreateSessionLogPath(DateTimeOffset startupTime) =>
        Path.Combine(
            LogsDirectory,
            string.Create(
                CultureInfo.InvariantCulture,
                $"LOG-{startupTime:yyyyMMdd-HHmmss}.log"));
}
