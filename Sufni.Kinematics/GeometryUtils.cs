namespace Sufni.Kinematics;

public static class GeometryUtils
{
    public static double CalculateDistance(IPoint p1, IPoint p2)
    {
        return Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
    }

    public static double CalculateAngleAtPoint(Joint central, Joint adjacent1, Joint adjacent2)
    {
        return CalculateAngleAtPoint(central.X, central.Y, adjacent1.X, adjacent1.Y, adjacent2.X, adjacent2.Y);
    }
    
    public static double CalculateAngleAtPoint(double centralX, double centralY,
        double adjacent1X, double adjacent1Y, double adjacent2X, double adjacent2Y)
    {
        // Create vectors from central to each adjacent point
        var (x1, y1) = (adjacent1X - centralX, adjacent1Y - centralY);
        var (x2, y2) = (adjacent2X - centralX, adjacent2Y - centralY);

        // Compute the dot product and magnitudes
        var dotProduct = x1 * x2 + y1 * y2;
        var magnitude1 = Math.Sqrt(x1 * x1 + y1 * y1);
        var magnitude2 = Math.Sqrt(x2 * x2 + y2 * y2);
        var magnitudeProduct = magnitude1 * magnitude2;

        if (magnitudeProduct == 0 || !double.IsFinite(magnitudeProduct))
        {
            throw new InvalidOperationException("Cannot calculate angle for coincident or degenerate points.");
        }

        var cosAngle = dotProduct / magnitudeProduct;
        if (!double.IsFinite(cosAngle))
        {
            throw new InvalidOperationException("Cannot calculate angle for coincident or degenerate points.");
        }

        // Clamp value to the valid range for Math.Acos to avoid NaN due to floating point errors
        cosAngle = Math.Clamp(cosAngle, -1.0, 1.0);
        return Math.Acos(cosAngle);
    }
}