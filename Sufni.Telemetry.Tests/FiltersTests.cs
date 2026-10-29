
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class FiltersTests
{
    [Theory]
    [InlineData(4)] // Even
    [InlineData(3)] // Less than 5
    public void Create_WithInvalidWindowSize_ThrowsArgumentException(int windowSize)
    {
        Assert.Throws<ArgumentException>(() => SavitzkyGolay.Create(windowSize, 0, 2));
    }

    [Fact]
    public void Create_WithNegativeDerivative_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SavitzkyGolay.Create(5, -1, 2));
    }

    [Fact]
    public void Create_WithNegativePolynomial_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SavitzkyGolay.Create(5, 0, -1));
    }

    [Fact]
    public void Create_WithSameParameters_ReturnsCachedInstance()
    {
        var first = SavitzkyGolay.Create(51, 1, 3);
        var second = SavitzkyGolay.Create(51, 1, 3);

        Assert.Same(first, second);
    }

    [Fact]
    public void Create_WithDifferentParameters_ReturnsDifferentInstances()
    {
        var first = SavitzkyGolay.Create(51, 1, 3);
        var second = SavitzkyGolay.Create(49, 1, 3);

        Assert.NotSame(first, second);
    }

    [Fact]
    public void Create_EvictsLeastRecentlyUsedFilter_ToStayWithinCoefficientBudget()
    {
        var mostRecent = SavitzkyGolay.Create(1001, 0, 0);
        var leastRecent = SavitzkyGolay.Create(1001, 0, 1);
        Assert.Same(mostRecent, SavitzkyGolay.Create(1001, 0, 0));

        _ = SavitzkyGolay.Create(307, 0, 0);

        Assert.Same(mostRecent, SavitzkyGolay.Create(1001, 0, 0));
        Assert.NotSame(leastRecent, SavitzkyGolay.Create(1001, 0, 1));
    }

    [Fact]
    public void Create_DoesNotRetainFilterLargerThanCoefficientBudget()
    {
        var first = SavitzkyGolay.Create(1449, 0, 0);
        var second = SavitzkyGolay.Create(1449, 0, 0);

        Assert.NotSame(first, second);
    }

    [Fact]
    public async Task Create_JoinsConcurrentOversizeRequests_WithoutRetainingResult()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return SavitzkyGolay.Create(1449, 0, 1);
            }))
            .ToArray();

        start.SetResult();
        var filters = await Task.WhenAll(tasks);
        var afterCompletion = SavitzkyGolay.Create(1449, 0, 1);

        Assert.True(
            filters.Distinct(ReferenceEqualityComparer.Instance).Count() < filters.Length,
            "Concurrent oversize requests should share in-flight construction.");
        Assert.NotSame(filters[0], afterCompletion);
    }

    [Fact]
    public async Task Create_JoinsConcurrentSameKeyRequests()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = Enumerable.Range(0, 8)
            .Select(_ => Task.Run(async () =>
            {
                await start.Task;
                return SavitzkyGolay.Create(225, 0, 1);
            }))
            .ToArray();

        start.SetResult();
        var filters = await Task.WhenAll(tasks);

        Assert.All(filters, filter => Assert.Same(filters[0], filter));
    }

    [Fact]
    public void Process_WithDataShorterThanWindow_ThrowsArgumentException()
    {
        var filter = SavitzkyGolay.Create(11, 0, 2);
        var data = new double[10];

        Assert.Throws<ArgumentException>(() => filter.Process(data, dt: 1));
    }

    [Fact]
    public void Process_LinearFunction_FirstDerivativeIsConstant()
    {
        const double dt = 0.25;
        var data = Enumerable.Range(0, 20).Select(i => 2 * i * dt).ToArray();
        var filter = SavitzkyGolay.Create(5, 1, 2);

        var result = filter.Process(data, dt);

        Assert.All(result, value => Assert.Equal(2.0, value, 1e-9));
    }

    [Fact]
    public void Process_QuadraticFunction_FirstDerivativeIsLinear()
    {
        const double dt = 0.25;
        var data = Enumerable.Range(0, 20).Select(i => Math.Pow(i * dt, 2)).ToArray();
        var filter = SavitzkyGolay.Create(5, 1, 2);

        var result = filter.Process(data, dt);

        for (var i = 0; i < data.Length; i++)
        {
            Assert.Equal(2.0 * i * dt, result[i], 1e-9);
        }
    }

    [Fact]
    public void Process_Smoothing_PreservesQuadraticSignal()
    {
        var data = Enumerable.Range(0, 20).Select(i => (double)i * i).ToArray();
        var filter = SavitzkyGolay.Create(5, 0, 2);

        var result = filter.Process(data, dt: 1);

        for (var i = 0; i < data.Length; i++)
        {
            Assert.Equal(data[i], result[i], 1e-9);
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(51)]
    public void Process_CubicFunction_PreservesDerivativeIncludingBoundaries(int windowSize)
    {
        const double dt = 0.001;
        var times = Enumerable.Range(0, Math.Max(128, windowSize * 4)).Select(i => i * dt).ToArray();
        var data = times.Select(t => 2 * t * t * t - 3 * t * t + 4 * t + 5).ToArray();
        var filter = SavitzkyGolay.Create(windowSize, 1, 3);

        var result = filter.Process(data, dt);

        for (var i = 0; i < times.Length; i++)
        {
            var t = times[i];
            Assert.Equal(6 * t * t - 6 * t + 4, result[i], 1e-8);
        }
    }

    [Fact]
    public void Process_NoisySignal_CalculatesVelocityAccurately()
    {
        // Arrange
        int count = 1000;
        var cleanSignal = new double[count];
        var noisyData = new double[count];
        var time = new double[count];
        var random = new Random(42);
        
        // Sine wave signal: P(t) = 100 * sin(2 * pi * t)
        // Expected Velocity (derivative): V(t) = 100 * 2 * pi * cos(2 * pi * t)
        for (int i = 0; i < count; i++)
        {
            var t = i / 1000.0; // 1ms samples
            time[i] = t;
            cleanSignal[i] = 100.0 * Math.Sin(2 * Math.PI * t);
            
            var noise = (random.NextDouble() * 2.0) - 1.0; // Random noise [-1.0, 1.0]
            noisyData[i] = cleanSignal[i] + noise;
        }

        // Window 51, Derivative 1, Poly 3
        var filter = SavitzkyGolay.Create(51, 1, 3);

        // Act
        var result = filter.Process(noisyData, dt: 0.001);

        // Assert
        var errors = new List<double>();
        for (int i = 100; i < count - 100; i++)
        {
            var expectedVelocity = 100.0 * 2 * Math.PI * Math.Cos(2 * Math.PI * time[i]);
            var error = Math.Abs(result[i] - expectedVelocity);
            errors.Add(error);
        }

        // Check aggregate error statistics as there are outliers e.g. around 0 crossings
        var meanError = errors.Average();
        var maxError = errors.Max();

        Assert.True(meanError < 15.0, $"Mean error {meanError:F2} units/s exceeds threshold");
        Assert.True(maxError < 40.0, $"Max error {maxError:F2} units/s exceeds threshold");
    }

}
