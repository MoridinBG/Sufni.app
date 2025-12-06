namespace Sufni.Kinematics;

public static class GeometryUtils
{
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

        var cosAngle = dotProduct / (magnitude1 * magnitude2);
        // Clamp value to the valid range for Math.Acos to avoid NaN due to floating point errors
        cosAngle = Math.Max(-1.0, Math.Min(1.0, cosAngle));
        return Math.Acos(cosAngle);
    }
}