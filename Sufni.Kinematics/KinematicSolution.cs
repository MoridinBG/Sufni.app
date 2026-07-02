using System.Collections.ObjectModel;

namespace Sufni.Kinematics;

public sealed class KinematicSolution
{
    private readonly JointPath[] jointPaths;
    private readonly ReadOnlyCollection<JointPath> jointPathView;
    private readonly Dictionary<string, JointPath> jointPathsByName;

    public KinematicSolution(IReadOnlyList<JointPath> jointPaths)
    {
        ArgumentNullException.ThrowIfNull(jointPaths);

        this.jointPaths = [.. jointPaths];
        jointPathView = Array.AsReadOnly(this.jointPaths);
        jointPathsByName = this.jointPaths.ToDictionary(path => path.JointName, StringComparer.Ordinal);
    }

    public IReadOnlyList<JointPath> JointPaths => jointPathView;

    public bool TryGetPath(string jointName, out JointPath path) => jointPathsByName.TryGetValue(jointName, out path!);

    public Dictionary<string, CoordinateList> ToCoordinateDictionary()
    {
        return jointPaths.ToDictionary(
            path => path.JointName,
            path => new CoordinateList([.. path.X], [.. path.Y]),
            StringComparer.Ordinal);
    }
}

public sealed record JointPath
{
    private readonly double[] x;
    private readonly double[] y;
    private readonly ReadOnlyCollection<double> xView;
    private readonly ReadOnlyCollection<double> yView;

    public JointPath(string jointName, IReadOnlyList<double> x, IReadOnlyList<double> y)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jointName);
        ArgumentNullException.ThrowIfNull(x);
        ArgumentNullException.ThrowIfNull(y);

        if (x.Count != y.Count)
        {
            throw new ArgumentException("Joint path X and Y coordinates must have the same count.", nameof(y));
        }

        JointName = jointName;
        this.x = [.. x];
        this.y = [.. y];
        xView = Array.AsReadOnly(this.x);
        yView = Array.AsReadOnly(this.y);
    }

    public string JointName { get; }

    public IReadOnlyList<double> X => xView;

    public IReadOnlyList<double> Y => yView;
}
