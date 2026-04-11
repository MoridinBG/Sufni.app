namespace Sufni.Kinematics;

public static class CoordinateRotation
{
    /// <summary>
    /// Rotates a point around a center point by the specified angle.
    /// </summary>
    public static (double x, double y) RotatePoint(
        double x, double y,
        double centerX, double centerY,
        double angleDegrees)
    {
        var rad = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(rad);
        var sin = Math.Sin(rad);

        var dx = x - centerX;
        var dy = y - centerY;

        var newX = centerX + dx * cos - dy * sin;
        var newY = centerY + dx * sin + dy * cos;

        return (newX, newY);
    }

    /// <summary>
    /// Rotates all points in place around a center point by the specified angle.
    /// CLockwise in screen coordinates
    /// </summary>
    public static void RotatePoints(
        IEnumerable<IPoint> points,
        double centerX, double centerY,
        double angleDegrees)
    {
        foreach (var point in points)
        {
            var (newX, newY) = RotatePoint(point.X, point.Y, centerX, centerY, angleDegrees);
            point.X = newX;
            point.Y = newY;
        }
    }

    /// <summary>
    /// Calculates the bounding box of a rectangle rotated around a center point.
    /// </summary>
    public static (double minX, double minY, double maxX, double maxY) GetRotatedBounds(
        double width, double height, double angleDegrees, double cx = 0, double cy = 0)
    {
        if (Math.Abs(angleDegrees) < 0.01) return (0, 0, width, height);

        var p1 = RotatePoint(0, 0, cx, cy, angleDegrees);
        var p2 = RotatePoint(width, 0, cx, cy, angleDegrees);
        var p3 = RotatePoint(width, height, cx, cy, angleDegrees);
        var p4 = RotatePoint(0, height, cx, cy, angleDegrees);

        var minX = Math.Min(Math.Min(p1.x, p2.x), Math.Min(p3.x, p4.x));
        var minY = Math.Min(Math.Min(p1.y, p2.y), Math.Min(p3.y, p4.y));
        var maxX = Math.Max(Math.Max(p1.x, p2.x), Math.Max(p3.x, p4.x));
        var maxY = Math.Max(Math.Max(p1.y, p2.y), Math.Max(p3.y, p4.y));

        return (minX, minY, maxX, maxY);
    }
}