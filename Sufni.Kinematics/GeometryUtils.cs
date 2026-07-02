namespace Sufni.Kinematics;

public static class GeometryUtils
{
    public static double CalculateDistance(IPoint p1, IPoint p2)
    {
        return double.Hypot(p2.X - p1.X, p2.Y - p1.Y);
    }

    public static double? CalculatePixelsToMillimetersFromChainstay(
        double? chainstayMillimeters,
        IPoint rearWheel,
        IPoint bottomBracket)
    {
        if (!chainstayMillimeters.HasValue ||
            chainstayMillimeters.Value <= 0 ||
            !double.IsFinite(chainstayMillimeters.Value))
        {
            return null;
        }

        var distancePixels = CalculateDistance(rearWheel, bottomBracket);
        if (distancePixels <= 0 || !double.IsFinite(distancePixels))
        {
            return null;
        }

        return chainstayMillimeters.Value / distancePixels;
    }

    public static double? CalculateHeadAngle(
        IPoint headTube1,
        IPoint headTube2,
        IPoint frontWheel,
        IPoint rearWheel,
        double frontWheelDiameter,
        double rearWheelDiameter,
        double pixelsToMillimeters)
    {
        if (pixelsToMillimeters <= 0 || !double.IsFinite(pixelsToMillimeters))
        {
            return null;
        }

        var frontRadiusPixels = frontWheelDiameter / 2.0 / pixelsToMillimeters;
        var rearRadiusPixels = rearWheelDiameter / 2.0 / pixelsToMillimeters;
        if (!double.IsFinite(frontRadiusPixels) || !double.IsFinite(rearRadiusPixels))
        {
            return null;
        }

        var frontContactY = frontWheel.Y + frontRadiusPixels;
        var rearContactY = rearWheel.Y + rearRadiusPixels;

        var dxGround = frontWheel.X - rearWheel.X;
        var dyGround = frontContactY - rearContactY;

        var top = headTube1.Y < headTube2.Y ? headTube1 : headTube2;
        var bottom = headTube1.Y < headTube2.Y ? headTube2 : headTube1;

        var dxHeadTube = top.X - bottom.X;
        var dyHeadTube = top.Y - bottom.Y;

        var magnitudeGround = double.Hypot(dxGround, dyGround);
        var magnitudeHeadTube = double.Hypot(dxHeadTube, dyHeadTube);
        if (magnitudeGround < 0.001 || magnitudeHeadTube < 0.001)
        {
            return null;
        }

        var dot = dxGround * dxHeadTube + dyGround * dyHeadTube;
        var cos = Math.Clamp(dot / (magnitudeGround * magnitudeHeadTube), -1.0, 1.0);
        var angle = Math.Acos(cos) * 180.0 / Math.PI;

        return Math.Round(180.0 - angle, 1);
    }

    public static double CalculateAngleAtPoint(double centralX, double centralY,
        double adjacent1X, double adjacent1Y, double adjacent2X, double adjacent2Y)
    {
        // Create vectors from central to each adjacent point
        var (x1, y1) = (adjacent1X - centralX, adjacent1Y - centralY);
        var (x2, y2) = (adjacent2X - centralX, adjacent2Y - centralY);

        // Compute the dot product and magnitudes
        var dotProduct = x1 * x2 + y1 * y2;
        var magnitude1 = double.Hypot(x1, y1);
        var magnitude2 = double.Hypot(x2, y2);
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
