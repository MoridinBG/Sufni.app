namespace Sufni.Kinematics;

/// Common interface for things that have X & Y to allow for common geometry methods
public interface IPoint
{
    double X { get; }
    double Y { get; }
}
