using System.Text.Json.Serialization;

namespace Sufni.Kinematics;

public class Link
{
    #region Public properties

    [JsonPropertyName("a")] public string? A_Name { get; set; }
    [JsonPropertyName("b")] public string? B_Name { get; set; }

    #endregion Public properties

    #region Public properties [non-json]
    
    [JsonIgnore] public Joint? A { get; set; }
    [JsonIgnore] public Joint? B { get; set; }

    #endregion Public properties [non-json]

    #region Constructors

    public Link() {}

    public Link(string a_name, string b_name)
    {
        A_Name = a_name;
        B_Name = b_name;
    }

    public Link(Joint a, Joint b)
    {
        A = a;
        B = b;
        A_Name = a.Name!;
        B_Name = b.Name!;
    }

    #endregion Constructors

    #region Public mehods

    public void ResolveJoints(Dictionary<string, Joint> pointMap)
    {
        if (A_Name is not null && pointMap.TryGetValue(A_Name, out var pointA))
        {
            A = pointA;
        }
        if (B_Name is not null && pointMap.TryGetValue(B_Name, out var pointB))
        {
            B = pointB;
        }
    }

    #endregion Public mehods

    #region Equality overrides / operators

    public override bool Equals(object? obj) => obj is not null && Equals(obj as Link);

    public bool Equals(Link? l)
    {
        // Optimization for a common false case.
        if (l is null) return false;

        // Optimization for a common true case.
        if (ReferenceEquals(this, l)) return true;

        // If run-time types are not exactly the same, return false.
        if (GetType() != l.GetType()) return false;

        // Return true if the fields match.
        return A_Name == l.A_Name && B_Name == l.B_Name;
    }

    public override int GetHashCode()
    {
        var hashA = A_Name?.GetHashCode();
        var hashB = B_Name?.GetHashCode();
        return HashCode.Combine(hashA, hashB);
    }

    public static bool operator ==(Link? lhs, Link? rhs)
    {
        if (lhs is not null) return lhs.Equals(rhs);
        return rhs is null;
    }

    public static bool operator !=(Link? lhs, Link? rhs) => !(lhs == rhs);

    #endregion Equality overrides / operators
}