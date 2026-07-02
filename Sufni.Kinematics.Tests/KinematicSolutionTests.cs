using Sufni.Kinematics;

namespace Sufni.Kinematics.Tests;

public class KinematicSolutionTests
{
    [Fact]
    public void Constructors_CopyIncomingCollections()
    {
        List<double> x = [0, 1];
        List<double> y = [2, 3];
        var path = new JointPath("rear", x, y);
        x[0] = 100;
        y[1] = 200;

        List<JointPath> paths = [path];
        var solution = new KinematicSolution(paths);
        paths[0] = new JointPath("front", [4], [5]);

        Assert.Equal(0, path.X[0]);
        Assert.Equal(3, path.Y[1]);
        Assert.True(solution.TryGetPath("rear", out var copiedPath));
        Assert.Same(path, copiedPath);
        Assert.False(solution.TryGetPath("front", out _));
    }
}
