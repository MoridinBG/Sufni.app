using Sufni.App.ExtensionHost.Contracts.RecordedSessionCatalog;

using Sufni.App.Extensibility.RecordedSessions;
namespace Sufni.App.Tests.Extensibility.RecordedSessions;

public class RecordedSessionDerivationWindowServiceTests
{
    [Fact]
    public async Task GetWindowsAsync_ReturnsEmpty_WhenNoSourcesAreRegistered()
    {
        var service = new RecordedSessionDerivationWindowService();

        Assert.Null(await service.GetWindowAsync(Guid.NewGuid()));
        Assert.Empty(await service.GetWindowsAsync());
        Assert.False(await service.IsRecordingSourceReferencedAsync(Guid.NewGuid()));
        Assert.Empty(await service.GetReferencedSourceSessionIdsAsync());
    }

    [Fact]
    public async Task GetWindowsAsync_AggregatesSources()
    {
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();
        var firstSourceId = Guid.NewGuid();
        var secondSourceId = Guid.NewGuid();
        var first = new TestWindowSource("first");
        first.Windows[firstSessionId] = new RecordedSessionDerivationWindow(firstSourceId, 1, 2);
        first.ReferencedSourceIds.Add(firstSourceId);
        var second = new TestWindowSource("second");
        second.Windows[secondSessionId] = new RecordedSessionDerivationWindow(secondSourceId, 3, null);
        second.ReferencedSourceIds.Add(secondSourceId);
        var service = new RecordedSessionDerivationWindowService([first, second]);

        var windows = await service.GetWindowsAsync();

        Assert.Equal(2, windows.Count);
        Assert.Equal(first.Windows[firstSessionId], windows[firstSessionId]);
        Assert.Equal(second.Windows[secondSessionId], windows[secondSessionId]);
        Assert.Equal(first.Windows[firstSessionId], await service.GetWindowAsync(firstSessionId));
        Assert.True(await service.IsRecordingSourceReferencedAsync(firstSourceId));
        var referencedSourceIds = await service.GetReferencedSourceSessionIdsAsync();
        Assert.Equal(2, referencedSourceIds.Count);
        Assert.Contains(firstSourceId, referencedSourceIds);
        Assert.Contains(secondSourceId, referencedSourceIds);
    }

    [Fact]
    public async Task GetWindowsAsync_RejectsDuplicateSessionWindows()
    {
        var sessionId = Guid.NewGuid();
        var first = new TestWindowSource("first");
        first.Windows[sessionId] = new RecordedSessionDerivationWindow(Guid.NewGuid(), 1, 2);
        var second = new TestWindowSource("second");
        second.Windows[sessionId] = new RecordedSessionDerivationWindow(Guid.NewGuid(), 3, 4);
        var service = new RecordedSessionDerivationWindowService([first, second]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetWindowsAsync());
    }

    [Fact]
    public void Constructor_RejectsDuplicateSourceExtensionIds()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            new RecordedSessionDerivationWindowService(
            [
                new TestWindowSource("duplicate"),
                new TestWindowSource("duplicate"),
            ]));

        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public void WindowsChanged_Raises_WhenSourceRaises()
    {
        var source = new TestWindowSource("source");
        var service = new RecordedSessionDerivationWindowService([source]);
        var raisedCount = 0;
        service.WindowsChanged += (_, _) => raisedCount++;

        source.RaiseWindowsChanged();

        Assert.Equal(1, raisedCount);
    }

    private sealed class TestWindowSource(string extensionId) : IRecordedSessionDerivationWindowSource
    {
        public string ExtensionId => extensionId;
        public Dictionary<Guid, RecordedSessionDerivationWindow> Windows { get; } = [];
        public HashSet<Guid> ReferencedSourceIds { get; } = [];
        public event EventHandler? WindowsChanged;

        public Task<RecordedSessionDerivationWindow?> GetWindowAsync(
            Guid sessionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Windows.GetValueOrDefault(sessionId));
        }

        public Task<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>> GetWindowsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyDictionary<Guid, RecordedSessionDerivationWindow>>(Windows);
        }

        public Task<bool> IsRecordingSourceReferencedAsync(
            Guid sourceSessionId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ReferencedSourceIds.Contains(sourceSessionId));
        }

        public Task<IReadOnlyCollection<Guid>> GetReferencedSourceSessionIdsAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyCollection<Guid>>(ReferencedSourceIds.ToArray());
        }

        public void RaiseWindowsChanged()
        {
            WindowsChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
