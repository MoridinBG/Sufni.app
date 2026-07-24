#if SUFNI_PROFILING_DIAGNOSTICS
using System;
using System.IO;
using System.Linq;
using Sufni.App.LiveDaq.Services;
using Sufni.Profiling;

namespace Sufni.App.Tests.LiveDaq.Services;

[Collection("ProfilingRuntime")]
public sealed class ProfilingBench01Tests
{
    [Fact]
    public async Task FinalizeAfterStop_WritesFinalizingMarkerAndSessionStop()
    {
        ProfilingRuntime.Shutdown();
        var outputPath = Path.Combine(
            Path.GetTempPath(),
            $"sufni-bench01-finalize-{Guid.NewGuid():N}.jsonl");
        ProfilingRuntime.Initialize(new ProfilingOptions(
            ProfilingMode.Timing,
            RunId: "finalize-test",
            OutputPath: outputPath,
            Corpus: ProfilingLiveDaqReplay.Corpus,
            AppDataPath: "finalize-test-app-data"));
        try
        {
            _ = ProfilingBench01.BeginSave();

            ProfilingBench01.FinalizeAfterStop("disconnected");

            Assert.False(ProfilingRuntime.IsEnabled);
            var records = (await File.ReadAllLinesAsync(outputPath))
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
            var finalizingIndex = Array.FindIndex(
                records,
                line => line.Contains("\"name\":\"Timing.Finalizing\"", StringComparison.Ordinal));
            var sessionStopIndex = Array.FindIndex(
                records,
                line => line.Contains("\"event\":\"SessionStop\"", StringComparison.Ordinal));
            Assert.True(finalizingIndex >= 0);
            Assert.Equal(records.Length - 1, sessionStopIndex);
            Assert.True(finalizingIndex < sessionStopIndex);
        }
        finally
        {
            ProfilingRuntime.Shutdown();
            File.Delete(outputPath);
        }
    }
}
#endif
