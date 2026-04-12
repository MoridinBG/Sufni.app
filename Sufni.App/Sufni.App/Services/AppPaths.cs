using System;
using System.Globalization;
using System.IO;

namespace Sufni.App.Services;

public static class AppPaths
{
    public static string AppDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Sufni.App");

    public static string DatabasePath { get; } = Path.Combine(AppDataDirectory, "sst.db");

    public static string LogsDirectory { get; } = Path.Combine(AppDataDirectory, "logs");

    public static string CertificatePath { get; } = Path.Combine(AppDataDirectory, "certificate.pfx");

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