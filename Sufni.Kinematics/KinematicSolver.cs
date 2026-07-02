using System.Diagnostics;
using Serilog;

namespace Sufni.Kinematics;

public readonly record struct CoordinateList(List<double> X, List<double> Y)
{
    public int Count => X.Count;
}

public class KinematicSolver
{
    private static readonly ILogger logger = Log.ForContext<KinematicSolver>();

    private readonly double shockMaxLength;
    private readonly ResolvedLinkage linkage;
    private readonly int steps;
    private readonly int iterations;

    public KinematicSolver(LinkageSpec linkage, int steps = 200, int iterations = 1000)
    {
        this.linkage = LinkageResolver.Resolve(linkage);

        this.steps = steps;
        this.iterations = iterations;
        shockMaxLength = this.linkage.Shock.Length;
    }

    #region Public methods

    public KinematicSolution SolveSuspensionMotion()
    {
        logger.Verbose(
            "Starting kinematic solve with {StepCount} steps, {IterationCount} iterations, and {JointCount} joints",
            steps,
            iterations,
            linkage.Joints.Count);

        var solutions = new Dictionary<string, CoordinateList>();

        for (var i = 0; i < steps; i++)
        {
            var compression = linkage.Spec.ShockStroke * i / (steps - 1);

            for (var it = 0; it < iterations; it++)
            {
                SolveConstraints(compression);
            }

            foreach (var joint in linkage.Joints)
            {
                var name = joint.Name!;
                if (!solutions.TryGetValue(name, out var value))
                {
                    value = new CoordinateList([], []);
                    solutions.Add(name, value);
                }

                value.X.Add(joint.X);
                value.Y.Add(joint.Y);
            }
        }

        logger.Verbose(
            "Kinematic solve completed with {JointSolutionCount} joint paths",
            solutions.Count);

        return new KinematicSolution(
            [.. solutions.Select(solution => new JointPath(solution.Key, solution.Value.X, solution.Value.Y))]);
    }

    #endregion Public methods

    #region Private methods

    private void SolveConstraints(double shockCompression)
    {
        var targetShockLength = shockMaxLength - shockCompression;
        EnforceLength(linkage.Shock, targetShockLength);

        foreach (var link in linkage.Links)
        {
            EnforceLength(link, link.Length);
        }
    }

    private static void EnforceLength(ResolvedLink link, double targetLength)
    {
        Debug.Assert(link.A is not null);
        Debug.Assert(link.B is not null);

        // If both ends are fixed, nothing to do
        if (link.A.IsFixed && link.B.IsFixed) return;

        var dx = link.B.X - link.A.X;
        var dy = link.B.Y - link.A.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);

        if (length < 1e-12) return;

        var diff = (length - targetLength) / length;
        var movableEndpointCount = (link.A.IsFixed ? 0 : 1) + (link.B.IsFixed ? 0 : 1);
        var correctionScale = movableEndpointCount == 1 ? 1.0 : 0.5;
        var correctionX = correctionScale * dx * diff;
        var correctionY = correctionScale * dy * diff;

        if (!link.A.IsFixed)
        {
            link.A.X += correctionX;
            link.A.Y += correctionY;
        }

        if (!link.B.IsFixed)
        {
            link.B.X -= correctionX;
            link.B.Y -= correctionY;
        }
    }

    #endregion Private methods
}
