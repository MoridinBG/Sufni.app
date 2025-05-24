using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public enum JointType
{
    Fixed,
    Floating,
    RearWheel,
    FrontWheel,
    BottomBracket
}

public class PolarCoordinate(double theta, double length)
{
    public double Theta  = theta;
    public double Length = length;
}

public class CartesianCoordinate(double x, double y)
{
    [JsonPropertyName("x")] public double X { get; set; } = x;
    [JsonPropertyName("y")] public double Y { get; set; } = y;

    public override bool Equals(object? obj) => obj is not null && Equals(obj as CartesianCoordinate);

    public bool Equals(CartesianCoordinate? p)
    {
        // Optimization for a common false case.
        if (p is null) return false;

        // Optimization for a common true case.
        if (ReferenceEquals(this, p)) return true;

        // If run-time types are not exactly the same, return false.
        if (GetType() != p.GetType()) return false;

        // Return true if the fields match.
        return Math.Abs(X - p.X) < 0.001 && Math.Abs(Y - p.Y) < 0.001;
    }

    public override int GetHashCode()
    {
        long hashX = Math.Round(X / 0.001).GetHashCode();
        long hashY = Math.Round(Y / 0.001).GetHashCode();
        return HashCode.Combine(hashX, hashY);
    }

    public static bool operator ==(CartesianCoordinate? lhs, CartesianCoordinate? rhs)
    {
        if (lhs is not null) return lhs.Equals(rhs);
        return rhs is null;
    }

    public static bool operator !=(CartesianCoordinate? lhs, CartesianCoordinate? rhs) => !(lhs == rhs);
}

public class Joint : CartesianCoordinate
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public JointType? Type { get; set; }

    public Joint() : base(0, 0) { }
    public Joint(string name, JointType type, double x, double y) : base(x, y)
    {
        Name = name;
        Type = type;
    }

    public Joint(double x, double y) : base(x, y)
    {
    }
    
    public override bool Equals(object? obj) => obj is not null && Equals(obj as Joint);

    public bool Equals(Joint? j)
    {
        // Optimization for a common false case.
        if (j is null) return false;

        // Optimization for a common true case.
        if (ReferenceEquals(this, j)) return true;

        // If run-time types are not exactly the same, return false.
        if (GetType() != j.GetType()) return false;

        // Return true if the fields match.
        return base.Equals(j) && j.Name == Name && j.Type == Type;
    }

    public override int GetHashCode()
    {
        var hashBase = base.GetHashCode();
        var hashName = Name?.GetHashCode();
        var hashType = Type?.GetHashCode();
        return HashCode.Combine(hashBase, hashName, hashType);
    }

    public static bool operator ==(Joint? lhs, Joint? rhs)
    {
        if (lhs is not null) return lhs.Equals(rhs);
        return rhs is null;
    }

    public static bool operator !=(Joint? lhs, Joint? rhs) => !(lhs == rhs);
}
