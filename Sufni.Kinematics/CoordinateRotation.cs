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
    /// Rotates all joints in place around a center point by the specified angle.
    /// CLockwise in screen coordinates
    /// </summary>
    public static void RotateJoints(
        List<Joint> joints,
        double centerX, double centerY,
        double angleDegrees)
    {
        foreach (var joint in joints)
        {
            var (newX, newY) = RotatePoint(joint.X, joint.Y, centerX, centerY, angleDegrees);
            joint.X = newX;
            joint.Y = newY;
        }
    }
}