namespace Sufni.Kinematics;

public static class GroundCalculator
{
    /// <summary>
    /// Calculates the rotation angle needed to level the ground based on wheel contact points.
    /// </summary>
    /// <returns>
    /// angleDegrees: The rotation angle to apply (negative to level ground horizontal).
    /// groundY: The Y coordinate of the ground line after rotation.
    /// </returns>
    public static (double angleDegrees, double groundY) CalculateGroundRotation(
        double frontWheelX, double frontWheelY, double frontWheelRadius,
        double rearWheelX, double rearWheelY, double rearWheelRadius)
    {
        var frontContactY = frontWheelY + frontWheelRadius;
        var rearContactY = rearWheelY + rearWheelRadius;
        
        var dx = frontWheelX - rearWheelX;
        var dy = frontContactY - rearContactY;
        var angleRadians = Math.Atan2(dy, dx);
        var angleDegrees = angleRadians * 180.0 / Math.PI;
        
        var rotationAngle = -angleDegrees;
        var groundY = Math.Max(frontContactY, rearContactY);

        return (rotationAngle, groundY);
    }
}