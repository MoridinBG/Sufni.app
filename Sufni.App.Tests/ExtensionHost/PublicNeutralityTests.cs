using System.Text.RegularExpressions;

namespace Sufni.App.Tests.ExtensionHost;

public class PublicNeutralityTests
{
    private static readonly HashSet<string> SearchableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".axaml",
        ".cs",
        ".csproj",
        ".json",
        ".md",
        ".props",
        ".targets",
        ".xaml",
        ".xml",
    };

    private static readonly HashSet<string> ExcludedDirectoryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        "bin",
        "obj",
        "TestResults",
    };

    // Current private capability vocabulary: module ids, class families,
    // table names, slot/contribution ids, and sync payload fields. This file
    // is the single allowed location for these tokens in the public repo.
    private static readonly Regex PrivateNamePattern = new(
        "video|strava|libvlc" +
        "|segment[-_ ]?analysis|session[-_ ]?segment|segment[-_ ]?match|match-segment" +
        "|segment[-_ ]?comparison|segments?[-_ ]?picker|segment[-_ ]?statistics" +
        "|segment[-_ ]?name[-_ ]?prompt|segment[-_ ]?recorded" +
        "|compare-statistics|save-current-selection",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    [Fact]
    public void PublicHost_DoesNotContainPrivateCapabilityNames()
    {
        var root = FindRepositoryRoot();
        var denyListFile = Path.Combine(root, "Sufni.App.Tests", "ExtensionHost", "PublicNeutralityTests.cs");

        var hits = EnumerateRepositoryFiles(root)
            .Where(IsSearchableFile)
            .Where(path => !string.Equals(path, denyListFile, StringComparison.Ordinal))
            .SelectMany(path => FindPrivateNameHits(root, path))
            .ToArray();

        Assert.True(
            hits.Length == 0,
            "Public host contains private capability names:" + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    private static IEnumerable<string> FindPrivateNameHits(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (PrivateNamePattern.IsMatch(line))
            {
                yield return $"{relativePath}:{lineNumber}:{line.Trim()}";
            }
        }
    }

    private static IEnumerable<string> EnumerateRepositoryFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            yield return file;
        }

        foreach (var subdirectory in Directory.EnumerateDirectories(directory))
        {
            if (ExcludedDirectoryNames.Contains(Path.GetFileName(subdirectory)))
            {
                continue;
            }

            foreach (var file in EnumerateRepositoryFiles(subdirectory))
            {
                yield return file;
            }
        }
    }

    private static bool IsSearchableFile(string path)
    {
        return SearchableExtensions.Contains(Path.GetExtension(path));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Sufni.App.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find repository root.");
    }
}
