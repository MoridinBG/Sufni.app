using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Sufni.Kinematics;

public readonly struct CoordinateList(List<double> x, List<double> y)
{
    public List<double> X { get; } = x;
    public List<double> Y { get; } = y;
    public int Count => X.Count;
}

public class KinematicSolver
{
    private readonly double shockMaxLength;
    private readonly Linkage linkage;
    private readonly int steps;
    private readonly int iterations;

    public KinematicSolver(Linkage linkage, int steps = 200, int iterations = 1000)
    {
        var linkageJson = linkage.ToJson();
        this.linkage = Linkage.FromJson(linkageJson);
        
        this.steps = steps;
        this.iterations = iterations;
        shockMaxLength = linkage.Shock.Length;
    }

    #region Public methods

    public Dictionary<string, CoordinateList> SolveSuspensionMotion()
    {
        var solutions = new Dictionary<string, CoordinateList>();

        for (var i = 0; i < steps; i++)
        {
            var compression = linkage.ShockStroke * i / (steps - 1);

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
        return solutions;
    }

    public static void ExportSolutionCsv(string filename, Dictionary<string, CoordinateList> solution)
    {
        if (solution.Count == 0)
            return;

        // Determine number of steps from first CoordinateList
        var steps = 0;
        foreach (var cl in solution.Values)
        {
            steps = cl.Count; break;
        }

        // Ordered joint names
        var jointNames = new List<string>(solution.Keys);
        jointNames.Sort();

        using var writer = new StreamWriter(filename, false, Encoding.UTF8);

        // --- Header ---
        var header = new StringBuilder();
        foreach (var name in jointNames)
        {
            header.Append(name).Append("_X,").Append(name).Append("_Y,");
        }
        header.Length--; // remove last comma
        writer.WriteLine(header.ToString());

        // --- Rows ---
        for (var step = 0; step < steps; step++)
        {
            var row = new StringBuilder();
            foreach (var name in jointNames)
            {
                var cl = solution[name];
                row.Append(cl.X[step].ToString(CultureInfo.InvariantCulture)).Append(',');
                row.Append(cl.Y[step].ToString(CultureInfo.InvariantCulture)).Append(',');
            }
            row.Length--; // remove last comma
            writer.WriteLine(row.ToString());
        }
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

    private static void EnforceLength(Link link, double targetLength)
    {
        Debug.Assert(link.A is not null);
        Debug.Assert(link.B is not null);
        
        // If both ends are fixed, nothing to do
        if (link.A.IsFixed && link.B.IsFixed) return;

        var dx = link.B.X - link.A.X;
        var dy = link.B.Y - link.A.Y;
        var length = Math.Sqrt(dx*dx + dy*dy);

        if (length < 1e-12) return;

        var diff = (length - targetLength) / length;
        var correctionX = 0.5 * dx * diff;
        var correctionY = 0.5 * dy * diff;

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