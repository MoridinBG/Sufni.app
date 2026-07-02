using Sufni.App.Infrastructure.Caching;

namespace Sufni.App.Tests.Infrastructure;

public class SingleFlightLruCacheTests
{
    [Fact]
    public void GetOrAdd_EvictsLeastRecentlyUsedEntry_WhenCapacityIsExceeded()
    {
        var created = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 2,
            key => Interlocked.Increment(ref created));

        var first = cache.GetOrAdd(1);
        var second = cache.GetOrAdd(2);
        var firstAgain = cache.GetOrAdd(1);
        var third = cache.GetOrAdd(3);
        var secondAgain = cache.GetOrAdd(2);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
        Assert.Equal(first, firstAgain);
        Assert.Equal(3, third);
        Assert.Equal(4, secondAgain);
    }

    [Fact]
    public async Task GetOrAdd_JoinsConcurrentSameKeyMisses()
    {
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 8,
            key =>
            {
                Interlocked.Increment(ref calls);
                factoryEntered.SetResult();
                releaseFactory.Task.GetAwaiter().GetResult();
                return key * 10;
            });
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable
            .Range(0, 16)
            .Select(_ => Task.Run(
                async () =>
                {
                    await start.Task;
                    return cache.GetOrAdd(7);
                }))
            .ToArray();

        start.SetResult();
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFactory.SetResult();
        var values = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.All(values, value => Assert.Equal(70, value));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void GetOrAdd_DoesNotRetainFailedFactoryResult()
    {
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 4,
            key =>
            {
                var attempt = Interlocked.Increment(ref calls);
                if (attempt == 1)
                {
                    throw new InvalidOperationException("boom");
                }

                return key * 10;
            });

        Assert.Throws<InvalidOperationException>(() => cache.GetOrAdd(3));
        var value = cache.GetOrAdd(3);

        Assert.Equal(30, value);
        Assert.Equal(2, calls);
    }
}
