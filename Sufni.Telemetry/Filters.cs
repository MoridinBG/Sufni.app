namespace Sufni.Telemetry;

public class SavitzkyGolay
{
    private readonly int windowSize;
    private readonly int derivative;
    private readonly int polynomial;
    private readonly double[][] weights;

    public SavitzkyGolay(int windowSize, int derivative, int polynomial)
    {
        this.windowSize = windowSize;
        this.derivative = derivative;
        this.polynomial = polynomial;
        weights = ComputeWeights();
    }

    public static SavitzkyGolay Create(int windowSize, int derivative, int polynomial)
    {
        if (windowSize % 2 == 0 || windowSize < 5)
        {
            throw new ArgumentException($"Window size [{windowSize}] must be odd and equal to or greater than 5");
        }

        if (derivative < 0)
        {
            throw new ArgumentException($"Derivative [{derivative}] must be equal or greater than 0");
        }

        if (polynomial < 0)
        {
            throw new ArgumentException($"Polynomial [{polynomial}] must be equal or greater than 0");
        }

        return new SavitzkyGolay(windowSize, derivative, polynomial);
    }

    public double[] Process(double[] data, double[] h)
    {
        if (windowSize > data.Length)
        {
            throw new ArgumentException($"Data length [{data.Length}] must be larger than window size [{windowSize}]");
        }

        var halfWindow = (int)Math.Floor(windowSize / 2.0);
        var numPoints = data.Length;
        var results = new double[numPoints];
        double hs;

        // For the borders
        for (var i = 0; i < halfWindow; i++)
        {
            var wg1 = weights[halfWindow - i - 1];
            var wg2 = weights[halfWindow + i + 1];
            var d1 = 0.0;
            var d2 = 0.0;

            for (var l = 0; l < windowSize; l++)
            {
                d1 += wg1[l] * data[l];
                d2 += wg2[l] * data[numPoints - windowSize + l];
            }

            hs = GetHs(h, halfWindow - i - 1, halfWindow);
            results[halfWindow - i - 1] = d1 / hs;

            hs = GetHs(h, numPoints - halfWindow + i, halfWindow);
            results[numPoints - halfWindow + i] = d2 / hs;
        }

        // For the internal points
        var wg = weights[halfWindow];
        for (var i = windowSize; i <= numPoints; i++)
        {
            var d = 0.0;
            for (var l = 0; l < windowSize; l++)
            {
                d += wg[l] * data[l + i - windowSize];
            }

            hs = GetHs(h, i - halfWindow - 1, halfWindow);
            results[i - halfWindow - 1] = d / hs;
        }

        return results;
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
}