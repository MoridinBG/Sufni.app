using System.Numerics.Tensors;

namespace Sufni.Telemetry;

public class SavitzkyGolay
{
    #region Private fields

    private const int CacheCapacity = 64;
    private const long CacheCoefficientBudgetBytes = 16 * 1024 * 1024;
    private static readonly System.Threading.Lock cacheGate = new();
    private static readonly Dictionary<CacheKey, CacheEntry> cache = [];
    private static readonly LinkedList<CacheKey> cacheRecency = [];
    private static readonly Dictionary<CacheKey, Lazy<SavitzkyGolay>> oversizeInFlight = [];
    private static long cachedCoefficientBytes;

    private readonly int windowSize;
    private readonly int derivative;
    private readonly int polynomial;
    private readonly double[][] weights;

    #endregion Private fields

    #region Constructors / Initializers

    private SavitzkyGolay(int windowSize, int derivative, int polynomial)
    {
        this.windowSize = windowSize;
        this.derivative = derivative;
        this.polynomial = polynomial;
        weights = ComputeWeights();
    }

    private readonly record struct CacheKey(int WindowSize, int Derivative, int Polynomial);

    private sealed class CacheEntry(
        SavitzkyGolay filter,
        LinkedListNode<CacheKey> recencyNode,
        long coefficientBytes)
    {
        public SavitzkyGolay Filter { get; } = filter;

        public LinkedListNode<CacheKey> RecencyNode { get; } = recencyNode;

        public long CoefficientBytes { get; } = coefficientBytes;
    }

    public static SavitzkyGolay Create(int windowSize, int derivative, int polynomial)
    {
        if (windowSize % 2 == 0 || windowSize < 5)
        {
            throw new ArgumentException($"Window size [{windowSize}] must be odd and equal to or greater than 5");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(derivative);

        ArgumentOutOfRangeException.ThrowIfNegative(polynomial);

        var key = new CacheKey(windowSize, derivative, polynomial);
        var coefficientBytes = checked((long)windowSize * windowSize * sizeof(double));
        if (coefficientBytes > CacheCoefficientBudgetBytes)
        {
            return CreateOversize(key, windowSize, derivative, polynomial);
        }

        lock (cacheGate)
        {
            if (cache.TryGetValue(key, out var entry))
            {
                cacheRecency.Remove(entry.RecencyNode);
                cacheRecency.AddFirst(entry.RecencyNode);
                return entry.Filter;
            }

            var filter = new SavitzkyGolay(windowSize, derivative, polynomial);
            while ((cache.Count >= CacheCapacity ||
                    cachedCoefficientBytes + coefficientBytes > CacheCoefficientBudgetBytes) &&
                   cacheRecency.Last is { } leastRecent)
            {
                RemoveCachedEntry(leastRecent);
            }

            var node = new LinkedListNode<CacheKey>(key);
            cacheRecency.AddFirst(node);
            cache[key] = new CacheEntry(filter, node, coefficientBytes);
            cachedCoefficientBytes += coefficientBytes;

            return filter;
        }
    }

    #endregion Constructors / Initializers

    #region Private methods

    private static SavitzkyGolay CreateOversize(
        CacheKey key,
        int windowSize,
        int derivative,
        int polynomial)
    {
        Lazy<SavitzkyGolay> lazy;
        lock (cacheGate)
        {
            if (!oversizeInFlight.TryGetValue(key, out lazy!))
            {
                lazy = new Lazy<SavitzkyGolay>(
                    () => new SavitzkyGolay(windowSize, derivative, polynomial),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                oversizeInFlight.Add(key, lazy);
            }
        }

        try
        {
            return lazy.Value;
        }
        finally
        {
            lock (cacheGate)
            {
                if (oversizeInFlight.TryGetValue(key, out var current) && ReferenceEquals(current, lazy))
                {
                    oversizeInFlight.Remove(key);
                }
            }
        }
    }

    private static void RemoveCachedEntry(LinkedListNode<CacheKey> node)
    {
        cacheRecency.Remove(node);
        if (cache.Remove(node.Value, out var entry))
        {
            cachedCoefficientBytes -= entry.CoefficientBytes;
        }
    }

    private double GetHs(double[] h, int center, int half)
    {
        var hs = 0.0;
        var count = 0;

        for (var i = center - half; i < center + half; i++)
        {
            if (i < 0 || i >= h.Length - 1) continue;
            hs += h[i + 1] - h[i];
            count++;
        }

        return Math.Pow(hs / count, derivative);
    }

    private static double GramPolynomial(int i, int m, int k, int s)
    {
        var result = k switch
        {
            > 0 => (4 * k - 2) / (k * (2 * m - k + 1.0)) *
                   (i * GramPolynomial(i, m, k - 1, s) + s * GramPolynomial(i, m, k - 1, s - 1)) -
                   (k - 1) * (2 * m + k) / (k * (2 * m - k + 1.0)) * GramPolynomial(i, m, k - 2, s),
            0 when s == 0 => 1.0,
            _ => 0.0
        };

        return result;
    }

    private static double ProductOfRange(int a, int b)
    {
        var gf = 1;

        if (a < b) return gf;
        for (var j = a - b + 1; j <= a; j++)
        {
            gf *= j;
        }

        return gf;
    }

    private static double PolyWeight(int i, int t, int windowMiddle, int polynomial, int derivative)
    {
        var sum = 0.0;

        for (var k = 0; k <= polynomial; k++)
        {
            sum +=
                (2 * k + 1) *
                (ProductOfRange(2 * windowMiddle, k) / ProductOfRange(2 * windowMiddle + k + 1, k + 1)) *
                GramPolynomial(i, windowMiddle, k, 0) * GramPolynomial(t, windowMiddle, k, derivative);
        }

        return sum;
    }

    private double[][] ComputeWeights()
    {
        var windowMiddle = (int)Math.Floor(windowSize / 2.0);
        var w = new double[windowSize][];

        for (var row = -windowMiddle; row <= windowMiddle; row++)
        {
            w[row + windowMiddle] = new double[windowSize];

            for (var col = -windowMiddle; col <= windowMiddle; col++)
            {
                w[row + windowMiddle][col + windowMiddle] = PolyWeight(col, row, windowMiddle, polynomial, derivative);
            }
        }

        return w;
    }

    private double[] ProcessCore(double[] data, double[]? h, double fixedDivisor)
    {
        if (windowSize > data.Length)
        {
            throw new ArgumentException($"Data length [{data.Length}] must be larger than window size [{windowSize}]");
        }

        var halfWindow = (int)Math.Floor(windowSize / 2.0);
        var numPoints = data.Length;
        var results = new double[numPoints];
        var useFixedDivisor = h is null;

        // For the borders
        var head = data.AsSpan(0, windowSize);                       // fixed left window
        var tail = data.AsSpan(numPoints - windowSize, windowSize);  // fixed right window
        for (var i = 0; i < halfWindow; i++)
        {
            var wg1 = weights[halfWindow - i - 1].AsSpan();
            var wg2 = weights[halfWindow + i + 1].AsSpan();
            var d1 = TensorPrimitives.Dot(wg1, head);
            var d2 = TensorPrimitives.Dot(wg2, tail);

            var leftIndex = halfWindow - i - 1;
            var leftDivisor = useFixedDivisor ? fixedDivisor : GetHs(h!, leftIndex, halfWindow);
            results[leftIndex] = d1 / leftDivisor;

            var rightIndex = numPoints - halfWindow + i;
            var rightDivisor = useFixedDivisor ? fixedDivisor : GetHs(h!, rightIndex, halfWindow);
            results[rightIndex] = d2 / rightDivisor;
        }

        // For the internal points
        var wg = weights[halfWindow];
        var wgSpan = wg.AsSpan();                                   // hoist coefficient row once
        for (var i = windowSize; i <= numPoints; i++)
        {
            var window = data.AsSpan(i - windowSize, windowSize);  // zero-alloc sliding window
            var d = TensorPrimitives.Dot(wgSpan, window);           // both spans length == windowSize
            var resultIndex = i - halfWindow - 1;
            var divisor = useFixedDivisor ? fixedDivisor : GetHs(h!, resultIndex, halfWindow);
            results[resultIndex] = d / divisor;
        }

        return results;
    }

    #endregion Private methods

    #region Public methods

    public double[] Process(double[] data, double[] h)
    {
        return ProcessCore(data, h, fixedDivisor: 0);
    }

    public double[] Process(double[] data, double dt)
    {
        var divisor = Math.Pow(dt, derivative);
        return ProcessCore(data, h: null, divisor);
    }

    #endregion Public methods
}
