
using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

public class FiltersTests
{
    [Fact]
    public void Create_WithValidParameters_ReturnsInstance()
    {
        var filter = SavitzkyGolay.Create(5, 0, 2);
        Assert.NotNull(filter);
    }

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
        Assert.Throws<ArgumentException>(() => SavitzkyGolay.Create(5, -1, 2));
    }

    [Fact]
    public void Create_WithNegativePolynomial_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => SavitzkyGolay.Create(5, 0, -1));
    }

    [Fact]
    public void Process_WithDataShorterThanWindow_ThrowsArgumentException()
    {
        var filter = SavitzkyGolay.Create(11, 0, 2);
        var data = new double[10];
        var h = new double[10];

        Assert.Throws<ArgumentException>(() => filter.Process(data, h));
    }

    [Fact]
    public void Process_LinearFunction_FirstDerivativeIsConstant()
    {
        // Arrange
        // f(x) = 2x. f'(x) = 2.
        int count = 20;
        var data = new double[count];
        var time = new double[count];
        for (int i = 0; i < count; i++)
        {
            time[i] = i; // 1 second steps
            data[i] = 2 * i;
        }

        // Window 5, Derivative 1, Polynomial 2
        var filter = SavitzkyGolay.Create(5, 1, 2);

        // Act
        var result = filter.Process(data, time);

        // Assert
        // With polynomial order >= 1, SG filter should preserve the 1st derivative of a linear function exactly (within float precision)
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(2.0, result[i], 1e-9);
        }
    }

    [Fact]
    public void Process_QuadraticFunction_FirstDerivativeIsLinear()
    {
        // Arrange
        // f(x) = x^2. f'(x) = 2x.
        int count = 20;
        var data = new double[count];
        var time = new double[count];
        for (int i = 0; i < count; i++)
        {
            time[i] = i;
            data[i] = i * i;
        }

        // Window 5, Derivative 1, Polynomial 2 (Quadratic can be fitted by poly order 2)
        var filter = SavitzkyGolay.Create(5, 1, 2);

        // Act
        var result = filter.Process(data, time);

        // Assert
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(2.0 * i, result[i], 1e-9); // Float tolerance
        }
    }

    [Fact]
    public void Process_Smoothing_PreservesSignalShape()
    {
        // Arrange
        // f(x) = x^2 (0 derivative)
        int count = 20;
        var data = new double[count];
        var time = new double[count];
        for (int i = 0; i < count; i++)
        {
            time[i] = i;
            data[i] = i * i;
        }

        // Window 5, Derivative 0, Polynomial 2
        var filter = SavitzkyGolay.Create(5, 0, 2);

        // Act
        var result = filter.Process(data, time);

        // Assert
        // Poly order 2 (parabollic) should perfectly smooth a quadratic signal
        for (int i = 0; i < count; i++)
        {
            Assert.Equal(data[i], result[i], 1e-9);
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
        var result = filter.Process(noisyData, time);

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