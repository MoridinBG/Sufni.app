using Sufni.App.Android;
using Sufni.App.Infrastructure;

namespace Sufni.App.Tests.Infrastructure;

public class AndroidServiceDiscoveryTests
{
    [Fact]
    public void StartStop_AcquiresAndReleasesOneLock_Idempotently()
    {
        var inner = new TestServiceDiscovery();
        var multicastLock = new TestMulticastLock();
        var sut = new AndroidServiceDiscovery(inner, new TestMulticastLockFactory(() => multicastLock));

        sut.StartBrowse("_gosst._tcp");
        sut.StartBrowse("_ignored._tcp");
        sut.StopBrowse();
        sut.StopBrowse();

        Assert.Equal(1, multicastLock.AcquireCount);
        Assert.Equal(1, multicastLock.ReleaseCount);
        Assert.Equal(1, multicastLock.DisposeCount);
        Assert.Equal(["_gosst._tcp"], inner.StartedTypes);
        Assert.Equal(1, inner.StopCount);
    }

    [Fact]
    public void StartFailure_ReleasesLockAndAllowsRetry()
    {
        var inner = new TestServiceDiscovery
        {
            StartException = new InvalidOperationException("start failed"),
        };
        var firstLock = new TestMulticastLock();
        var secondLock = new TestMulticastLock();
        var locks = new Queue<TestMulticastLock>([firstLock, secondLock]);
        var sut = new AndroidServiceDiscovery(
            inner,
            new TestMulticastLockFactory(() => locks.Dequeue()));

        Assert.Throws<InvalidOperationException>(() => sut.StartBrowse("_gosst._tcp"));
        Assert.Equal(1, firstLock.ReleaseCount);
        Assert.Equal(1, firstLock.DisposeCount);

        inner.StartException = null;
        sut.StartBrowse("_gosst._tcp");
        sut.StopBrowse();

        Assert.Equal(2, inner.StartCount);
        Assert.Equal(1, secondLock.ReleaseCount);
        Assert.Equal(1, secondLock.DisposeCount);
    }

    [Fact]
    public void StopFailure_ReleasesLockAndAllowsRetry()
    {
        var inner = new TestServiceDiscovery
        {
            StopException = new InvalidOperationException("stop failed"),
        };
        var firstLock = new TestMulticastLock();
        var secondLock = new TestMulticastLock();
        var locks = new Queue<TestMulticastLock>([firstLock, secondLock]);
        var sut = new AndroidServiceDiscovery(
            inner,
            new TestMulticastLockFactory(() => locks.Dequeue()));

        sut.StartBrowse("_gosst._tcp");
        Assert.Throws<InvalidOperationException>(() => sut.StopBrowse());
        Assert.Equal(1, firstLock.ReleaseCount);
        Assert.Equal(1, firstLock.DisposeCount);

        inner.StopException = null;
        sut.StartBrowse("_gosst._tcp");
        sut.StopBrowse();

        Assert.Equal(2, inner.StartCount);
        Assert.Equal(2, inner.StopCount);
        Assert.Equal(1, secondLock.ReleaseCount);
        Assert.Equal(1, secondLock.DisposeCount);
    }

    [Fact]
    public void LockAcquireFailure_DisposesCandidateAndStillStartsBrowse()
    {
        var inner = new TestServiceDiscovery();
        var multicastLock = new TestMulticastLock
        {
            AcquireException = new InvalidOperationException("lock failed"),
        };
        var sut = new AndroidServiceDiscovery(inner, new TestMulticastLockFactory(() => multicastLock));

        sut.StartBrowse("_gosst._tcp");
        sut.StopBrowse();

        Assert.Equal(1, multicastLock.AcquireCount);
        Assert.Equal(0, multicastLock.ReleaseCount);
        Assert.Equal(1, multicastLock.DisposeCount);
        Assert.Equal(1, inner.StartCount);
        Assert.Equal(1, inner.StopCount);
    }

    [Fact]
    public void MissingLock_StillStartsBrowse()
    {
        var inner = new TestServiceDiscovery();
        var sut = new AndroidServiceDiscovery(inner, new TestMulticastLockFactory(() => null));

        sut.StartBrowse("_gosst._tcp");
        sut.StopBrowse();

        Assert.Equal(1, inner.StartCount);
        Assert.Equal(1, inner.StopCount);
    }

    [Fact]
    public void LockReleaseFailure_StillDisposesLock()
    {
        var inner = new TestServiceDiscovery();
        var multicastLock = new TestMulticastLock
        {
            ReleaseException = new InvalidOperationException("release failed"),
        };
        var sut = new AndroidServiceDiscovery(inner, new TestMulticastLockFactory(() => multicastLock));

        sut.StartBrowse("_gosst._tcp");
        sut.StopBrowse();

        Assert.Equal(1, multicastLock.ReleaseCount);
        Assert.Equal(1, multicastLock.DisposeCount);
    }

    private sealed class TestMulticastLockFactory(
        Func<IAndroidMulticastLock?> create) : IAndroidMulticastLockFactory
    {
        public IAndroidMulticastLock? Create() => create();
    }

    private sealed class TestMulticastLock : IAndroidMulticastLock
    {
        public Exception? AcquireException { get; init; }
        public Exception? ReleaseException { get; init; }
        public int AcquireCount { get; private set; }
        public int ReleaseCount { get; private set; }
        public int DisposeCount { get; private set; }
        public bool IsHeld { get; private set; }

        public void Acquire()
        {
            AcquireCount++;
            if (AcquireException is not null)
            {
                throw AcquireException;
            }

            IsHeld = true;
        }

        public void Release()
        {
            ReleaseCount++;
            if (ReleaseException is not null)
            {
                throw ReleaseException;
            }

            IsHeld = false;
        }

        public void Dispose()
        {
            DisposeCount++;
            IsHeld = false;
        }
    }

    private sealed class TestServiceDiscovery : IServiceDiscovery
    {
        public event EventHandler<ServiceAnnouncementEventArgs>? ServiceAdded
        {
            add { }
            remove { }
        }

        public event EventHandler<ServiceAnnouncementEventArgs>? ServiceRemoved
        {
            add { }
            remove { }
        }

        public Exception? StartException { get; set; }
        public Exception? StopException { get; set; }
        public List<string> StartedTypes { get; } = [];
        public int StartCount => StartedTypes.Count;
        public int StopCount { get; private set; }

        public void StartBrowse(string type)
        {
            StartedTypes.Add(type);
            if (StartException is not null)
            {
                throw StartException;
            }
        }

        public void StopBrowse()
        {
            StopCount++;
            if (StopException is not null)
            {
                throw StopException;
            }
        }
    }
}
