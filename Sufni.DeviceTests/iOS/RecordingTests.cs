using Microsoft.Extensions.DependencyInjection;
using Sufni.App;
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Services;
using Sufni.App.Shell.Services.Preferences;
using Sufni.App.SyncAndPairing.Services;
using Sufni.App.Acquisition.Models;
using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Processing.RecordedSessionProjection;
using Sufni.Telemetry;
using Sufni.Telemetry.Tests;

namespace Sufni.DeviceTests;

[TestClass]
public sealed class RecordingTests
{
    [TestMethod]
    public async Task ImportedRecordingReopensWithItsOriginalContentAndIdentity()
    {
        var fixture = GoldenTelemetryCompatibilityFixtures.SstV5;
        using var file = new TelemetryFileSource("ride.SST", fixture.GetBytes());
        var source = RecordedSessionSourceFactory.CreateImportedSst(file);
        var directory = Directory.CreateTempSubdirectory();
        var session = new Session(source.SessionId, "ride", "", null);
        var root = new PlatformRoot(
            new AppCapabilities(false, false, false, false, false, false),
            new InputCapabilities(false, true, false, true, CommandModifier.Meta),
            new LaunchPreferences(UiLayoutProfile.Compact), _ => { }, new AppPaths(directory.FullName));
        try
        {
            await using (var services = Open(root))
            {
                var result = await services.GetRequiredService<ISessionRepository>()
                    .ImportProcessedSessionAsync(session, null, source);
                Assert.IsInstanceOfType<SessionImportCommitResult.Inserted>(result);
            }

            await using var reopened = Open(root);
            var saved = await reopened.GetRequiredService<IRecordedSessionSourceRepository>()
                .GetRecordedSessionSourceAsync(source.SessionId);
            Assert.IsNotNull(saved);
            Assert.AreEqual(fixture.Sha256, saved.SourceHash);
            var bytes = RecordedSessionSourcePayloadCodec.DecompressImportedSst(saved.Payload);
            CollectionAssert.AreEqual(fixture.GetBytes(), bytes);
            Assert.AreEqual((byte)5, RawTelemetryData.FromByteArray(bytes).Version);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static ServiceProvider Open(PlatformRoot root) => new ServiceCollection()
        .AddSharedServices(root)
        .AddSingleton<ILocalDeviceIdentity, TestIdentity>()
        .BuildServiceProvider();

    private sealed class TestIdentity : ILocalDeviceIdentity
    {
        public Task<string> GetIdAsync() => Task.FromResult("ios-smoke");
    }
}
