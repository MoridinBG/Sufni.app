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
    public void GetOrAdd_EvictsByWeight_AndPreservesRecentEntry()
    {
        var calls = new Dictionary<int, int>();
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 8,
            maximumWeight: 6,
            valueWeight: static value => value);

        int Create(int key)
        {
            calls[key] = calls.GetValueOrDefault(key) + 1;
            return key;
        }

        var first = cache.GetOrAdd(2, Create);
        var leastRecent = cache.GetOrAdd(3, Create);
        Assert.Equal(first, cache.GetOrAdd(2, Create));
        _ = cache.GetOrAdd(4, Create);
        var firstAgain = cache.GetOrAdd(2, Create);
        var reloaded = cache.GetOrAdd(3, Create);

        Assert.Equal(first, firstAgain);
        Assert.Equal(leastRecent, reloaded);
        Assert.Equal(1, calls[2]);
        Assert.Equal(2, calls[3]);
    }

    [Fact]
    public void GetOrAdd_ZeroWeightValuesRemainEntryBounded()
    {
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 2,
            maximumWeight: 10,
            valueWeight: static _ => 0);

        int Create(int _) => Interlocked.Increment(ref calls);

        _ = cache.GetOrAdd(1, Create);
        _ = cache.GetOrAdd(2, Create);
        _ = cache.GetOrAdd(3, Create);
        _ = cache.GetOrAdd(1, Create);

        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task GetOrAddAsync_SharesOversizeValueWithCurrentWaiters_WithoutRetainingIt()
    {
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<List<int>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new SingleFlightLruCache<int, List<int>>(
            capacity: 8,
            maximumWeight: 3,
            valueWeight: static value => value.Count);

        Task<List<int>> Factory(int _, CancellationToken __)
        {
            Interlocked.Increment(ref calls);
            factoryEntered.TrySetResult();
            return releaseFactory.Task;
        }

        var firstRead = cache.GetOrAddAsync(1, Factory);
        var secondRead = cache.GetOrAddAsync(1, Factory);
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var oversize = new List<int> { 1, 2, 3, 4 };
        releaseFactory.SetResult(oversize);
        var shared = await Task.WhenAll(firstRead, secondRead);
        var rebuilt = await cache.GetOrAddAsync(
            1,
            (_, _) =>
            {
                Interlocked.Increment(ref calls);
                return Task.FromResult(new List<int> { 5, 6, 7, 8 });
            });

        Assert.All(shared, value => Assert.Same(oversize, value));
        Assert.NotSame(oversize, rebuilt);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrAddAsync_WeightEvictionSkipsInFlightEntries()
    {
        var slowEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSlow = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowCalls = 0;
        var heavyCalls = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 8,
            maximumWeight: 10,
            valueWeight: static value => value);

        var slow = cache.GetOrAddAsync(
            2,
            (_, _) =>
            {
                Interlocked.Increment(ref slowCalls);
                slowEntered.TrySetResult();
                return releaseSlow.Task;
            });
        await slowEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        _ = await cache.GetOrAddAsync(
            1,
            (_, _) =>
            {
                Interlocked.Increment(ref heavyCalls);
                return Task.FromResult(10);
            });
        _ = await cache.GetOrAddAsync(3, (_, _) => Task.FromResult(1));

        releaseSlow.SetResult(1);
        var slowValue = await slow;
        var slowAgain = await cache.GetOrAddAsync(2, (_, _) => Task.FromResult(99));
        _ = await cache.GetOrAddAsync(
            1,
            (_, _) =>
            {
                Interlocked.Increment(ref heavyCalls);
                return Task.FromResult(10);
            });

        Assert.Equal(1, slowValue);
        Assert.Equal(slowValue, slowAgain);
        Assert.Equal(1, slowCalls);
        Assert.Equal(2, heavyCalls);
    }

    [Fact]
    public async Task GetOrAddAsync_DoesNotRetainValue_WhenWeightCalculationFails()
    {
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 8,
            maximumWeight: 10,
            valueWeight: static _ => throw new InvalidOperationException("weight"));

        Task<int> Factory(int _, CancellationToken __)
        {
            Interlocked.Increment(ref calls);
            return Task.FromResult(1);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync(1, Factory));
        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync(1, Factory));

        Assert.Equal(2, calls);
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
    public async Task GetOrAddAsync_JoinsConcurrentSameKeyMisses()
    {
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(capacity: 8);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable
            .Range(0, 16)
            .Select(_ => Task.Run(
                async () =>
                {
                    await start.Task;
                    return await cache.GetOrAddAsync(
                        7,
                        (key, _) =>
                        {
                            Interlocked.Increment(ref calls);
                            factoryEntered.TrySetResult();
                            return releaseFactory.Task;
                        });
                }))
            .ToArray();

        start.SetResult();
        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        releaseFactory.SetResult(70);
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

    [Fact]
    public async Task GetOrAddAsync_DoesNotRetainFaultedFactoryTask()
    {
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(capacity: 4);

        Task<int> Factory(int key, CancellationToken _)
        {
            var attempt = Interlocked.Increment(ref calls);
            return attempt == 1
                ? Task.FromException<int>(new InvalidOperationException("boom"))
                : Task.FromResult(key * 10);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => cache.GetOrAddAsync(3, Factory));
        var value = await cache.GetOrAddAsync(3, Factory);

        Assert.Equal(30, value);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrAddAsync_CallerCancellationDoesNotEvictInFlightFactoryTask()
    {
        var factoryEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFactory = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var cache = new SingleFlightLruCache<int, int>(capacity: 4);

        Task<int> Factory(int key, CancellationToken _)
        {
            Interlocked.Increment(ref calls);
            factoryEntered.TrySetResult();
            return releaseFactory.Task;
        }

        using var cancellation = new CancellationTokenSource();
        var canceledRead = cache.GetOrAddAsync(7, Factory, cancellation.Token);

        await factoryEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledRead);

        var sharedRead = cache.GetOrAddAsync(7, Factory);
        releaseFactory.SetResult(70);
        var value = await sharedRead.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(70, value);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Remove_EvictsSpecificEntry()
    {
        var created = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 4,
            key => Interlocked.Increment(ref created));

        var first = cache.GetOrAdd(1);
        cache.Remove(1);
        var second = cache.GetOrAdd(1);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public void RemoveWithValue_EvictsOnlyMatchingEntry()
    {
        var created = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 4,
            key => Interlocked.Increment(ref created));

        var first = cache.GetOrAdd(1);
        cache.Remove(1, first + 1);
        var stillCached = cache.GetOrAdd(1);
        cache.Remove(1, first);
        var reloaded = cache.GetOrAdd(1);

        Assert.Equal(first, stillCached);
        Assert.NotEqual(first, reloaded);
    }

    [Fact]
    public void RemoveWhere_EvictsMatchingEntries()
    {
        var created = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 8,
            key => Interlocked.Increment(ref created));

        var first = cache.GetOrAdd(1);
        var second = cache.GetOrAdd(2);
        cache.RemoveWhere(key => key == 1);
        var firstReloaded = cache.GetOrAdd(1);
        var secondStillCached = cache.GetOrAdd(2);

        Assert.NotEqual(first, firstReloaded);
        Assert.Equal(second, secondStillCached);
    }

    [Fact]
    public void Clear_EvictsAllEntries()
    {
        var created = 0;
        var cache = new SingleFlightLruCache<int, int>(
            capacity: 8,
            key => Interlocked.Increment(ref created));

        var first = cache.GetOrAdd(1);
        var second = cache.GetOrAdd(2);
        cache.Clear();
        var firstReloaded = cache.GetOrAdd(1);
        var secondReloaded = cache.GetOrAdd(2);

        Assert.NotEqual(first, firstReloaded);
        Assert.NotEqual(second, secondReloaded);
    }
}
