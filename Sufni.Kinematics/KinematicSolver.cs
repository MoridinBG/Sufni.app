using System.Diagnostics;

namespace Sufni.Kinematics;

/// <summary>Solves a linkage using private scratch state and returns immutable joint paths.</summary>
public static class KinematicSolver
{
    public static KinematicSolution Solve(LinkageSpec spec, int steps = 200, int iterations = 1000)
    {
        var linkage = LinkageResolver.Resolve(spec);
        var shockMaxLength = linkage.Shock.Length;
        var solutions = new Dictionary<string, (List<double> X, List<double> Y)>();

        for (var i = 0; i < steps; i++)
        {
            var compression = linkage.Spec.ShockStroke * i / (steps - 1);
            for (var it = 0; it < iterations; it++)
            {
                EnforceLength(linkage.Shock, shockMaxLength - compression);
                foreach (var link in linkage.Links)
                {
                    EnforceLength(link, link.Length);
                }
            }

            foreach (var joint in linkage.Joints)
            {
                if (!solutions.TryGetValue(joint.Name, out var value))
                {
                    value = ([], []);
                    solutions.Add(joint.Name, value);
                }
                value.X.Add(joint.X);
                value.Y.Add(joint.Y);
            }
        }

        return new KinematicSolution(
            [.. solutions.Select(solution => new JointPath(solution.Key, solution.Value.X, solution.Value.Y))]);
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
}
