namespace Sufni.Kinematics;

/// <summary>A published coordinate series, isolated from its input buffers.</summary>
public readonly record struct CoordinateList
{
    public CoordinateList(IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        X = Array.AsReadOnly<double>([.. x]);
        Y = Array.AsReadOnly<double>([.. y]);
    }

    public IReadOnlyList<double> X { get; }
    public IReadOnlyList<double> Y { get; }
    public int Count => X.Count;
}
