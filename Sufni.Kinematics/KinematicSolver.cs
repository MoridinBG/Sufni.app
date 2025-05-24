using System.Diagnostics;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.Optimization;
using QuikGraph;
using QuikGraph.Algorithms;

namespace Sufni.Kinematics;

public readonly struct CoordinateList(List<double> x, List<double> y)
{
    public List<double> X { get; } = x;
    public List<double> Y { get; } = y;
}

internal static class NumericalGradient
{
    private const double StepSize = 1e-8;

    public static Vector<double> ComputeGradient(Func<Vector<double>, double> func, Vector<double> x)
    {
        var n = x.Count;
        var gradient = Vector<double>.Build.Dense(n);

        for (var i = 0; i < n; i++)
        {
            var xStep = x.Clone();
            xStep[i] += StepSize;

            var fXPlusH = func(xStep);
            var fX = func(x);

            gradient[i] = (fXPlusH - fX) / StepSize;
        }

        return gradient;
    }
}

internal class EarlyStopException(Vector<double> x) : Exception
{
    public Vector<double> X { get; } = x;
}

internal class CartesianJoints
{
    #region Public fields

    public readonly List<Joint> KinematicLoopJoints;
    public readonly List<Joint> EndEffectorJoints;
    public readonly List<Joint> StaticJoints;
    public readonly Joint KinematicLoopOffset;
    public readonly int[] ShockEyeIndices = new int[2];
    public readonly JointType[] ShockEyeTypes = new JointType[2];
    public readonly double ShockMaxLength;

    #endregion Public fields

    #region Constructors

    public CartesianJoints(List<Joint> joints, List<Link> links, Link shock)
    {
        // Classify joints
        KinematicLoopJoints = FindKinematicLoop(joints, links);
        StaticJoints = [.. joints.Where(p => p.Type is JointType.FrontWheel or JointType.Fixed or JointType.BottomBracket)];
        EndEffectorJoints = [.. joints.Where(p => !KinematicLoopJoints.Contains(p) && !StaticJoints.Contains(p))];
        KinematicLoopOffset = KinematicLoopJoints[0];

        // Find shock eye indices
        ShockEyeTypes[0] = shock.A!.Type!.Value;
        if (EndEffectorJoints.Contains(shock.A!))
        {
            ShockEyeIndices[0] = EndEffectorJoints.IndexOf(shock.A!) + KinematicLoopJoints.Count;
        }
        else if (KinematicLoopJoints.Contains(shock.A!))
        {
            ShockEyeIndices[0] = KinematicLoopJoints.IndexOf(shock.A!);
        }
        else if (StaticJoints.Contains(shock.A!))
        {
            ShockEyeIndices[0] = StaticJoints.IndexOf(shock.A!);
        }

        ShockEyeTypes[1] = shock.B!.Type!.Value;
        if (EndEffectorJoints.Contains(shock.B!))
        {
            ShockEyeIndices[1] = EndEffectorJoints.IndexOf(shock.B!) + KinematicLoopJoints.Count;
        }
        else if (KinematicLoopJoints.Contains(shock.B!))
        {
            ShockEyeIndices[1] = KinematicLoopJoints.IndexOf(shock.B!);
        }
        else if (StaticJoints.Contains(shock.B!))
        {
            ShockEyeIndices[1] = StaticJoints.IndexOf(shock.B!);
        }

        // Calculate shock maximum length
        var dx = shock.A!.X - shock.B!.X;
        var dy = shock.A!.Y - shock.B!.Y;
        ShockMaxLength = Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion Constructors

    #region Private methods

    private static List<Joint> FindKinematicLoop(List<Joint> joints, List<Link> links)
    {
        var grounds = joints.Where(p => p.Type == JointType.Fixed || p.Type == JointType.FrontWheel).ToArray();
        var graph = new UndirectedGraph<Joint, Edge<Joint>>();
        joints.ForEach(j => graph.AddVertex(j));
        links.ForEach(l => graph.AddEdge(new Edge<Joint>(l.A!, l.B!)));

        // Loop between grounds, and find the one connected by links. This will
        // find the first set of grounds with a valid path between them. This is
        // fine for 4 bar where there is only one set of grounds with valid
        // path. Needs additional logic for 6-bar plus TBC
        for (var i = 0; i < grounds.Length; i++)
        {
            for (var j = i + 1; j < grounds.Length; j++)
            {
                // Find shortest path between these two grounds
                var tryGetPath = graph.ShortestPathsDijkstra(_ => 1, grounds[i]);
                if (tryGetPath(grounds[j], out var path))
                {
                    var loop = new List<Joint>() { grounds[i] };
                    foreach (var edge in path)
                    {
                        var toAdd = edge.Source == loop[^1] ? edge.Target : edge.Source;
                        loop.Add(toAdd);
                    }

                    if (!GeometryUtils.IsCounterClockwise(loop)) loop.Reverse();
                    return loop;
                }
            }
        }

        return [];
    } 

    #endregion Private methods
}

internal class PolarJoints
{
    #region Public fields

    public readonly List<PolarCoordinate> KinematicLoopPolar;
    public readonly List<PolarCoordinate> EndEffectorPolar;
    public readonly List<int> EndEffectorOffsetFromIndices;

    #endregion Public fields

    #region Constructors

    public PolarJoints(CartesianJoints cartesianJoints, List<Link> links)
    {
        (KinematicLoopPolar, EndEffectorPolar, EndEffectorOffsetFromIndices) = GetSolutionSpaceVectors(cartesianJoints, links);
    }

    #endregion Constructors

    #region Private methods

    private static (List<PolarCoordinate>, List<PolarCoordinate>, List<int>) GetSolutionSpaceVectors(CartesianJoints classifiedJoints, List<Link> links)
    {
        var kinematicLoopPolar = CartesianToPolar(classifiedJoints.KinematicLoopJoints, true);
        var endEffectorPolar = new List<PolarCoordinate>();
        var endEffectorOffsetFromIndices = new List<int>();

        // Loop through end effector Joints and find attachment Joint and offset
        foreach (var eep in classifiedJoints.EndEffectorJoints)
        {
            // Find attachment Joint
            var attachJointIndex = FindEndEffectorAttachJoint(classifiedJoints.KinematicLoopJoints, links, eep);

            // Find offset from attach Joint to end effector
            var offset = CartesianToPolar([classifiedJoints.KinematicLoopJoints[attachJointIndex], eep])[0];

            // Find constant offset from link, original Th was theta from global
            var theta = kinematicLoopPolar[attachJointIndex].Theta - offset.Theta;

            // Store in expected format
            endEffectorOffsetFromIndices.Add(attachJointIndex);
            endEffectorPolar.Add(new PolarCoordinate(theta, offset.Length));
        }

        return (kinematicLoopPolar, endEffectorPolar, endEffectorOffsetFromIndices);
    }

    private static int FindEndEffectorAttachJoint(List<Joint> kinematicLoopJoints, List<Link> links, Joint endEffJoint)
    {
        List<int> possibleLinks = [];

        // Iterate over the links to find the matching attachment Joint
        foreach (var link in links)
        {
            if (link.A! == endEffJoint)
            {
                possibleLinks.Add(kinematicLoopJoints.IndexOf(link.B!));
            }
            if (link.B! == endEffJoint)
            {
                possibleLinks.Add(kinematicLoopJoints.IndexOf(link.A!));
            }
        }

        if (possibleLinks.Count == 0)
        {
            throw new ArgumentException("No attachment Joint found for the given end-effector Joint.");
        }

        return possibleLinks.Min();
    }

    private static List<PolarCoordinate> CartesianToPolar(List<Joint> cartesian, bool isLoop = false)
    {
        var diff = new List<Joint>();

        for (var i = 1; i < cartesian.Count; ++i)
        {
            var dx = cartesian[i].X - cartesian[i - 1].X;
            var dy = cartesian[i].Y - cartesian[i - 1].Y;
            diff.Add(new Joint(dx, dy));
        }

        // Add first Joint to end of list again if 'loop' is specified
        if (isLoop)
        {
            var dx = cartesian[0].X - cartesian[^1].X;
            var dy = cartesian[0].Y - cartesian[^1].Y;
            diff.Add(new Joint(dx, dy));
        }

        return [.. diff.Select(d => new PolarCoordinate(
            Math.Atan2(d.Y, d.X),
            Math.Sqrt(d.X * d.X + d.Y * d.Y)))];
    }

    #endregion Private methods
}

public class KinematicSolver
{
    #region Private fields

    private readonly int steps;
    private readonly CartesianJoints cartesianJoints;
    private readonly List<Link> links;
    private PolarJoints? polarJoints;
    private readonly double shockStroke;

    #endregion Private fields

    #region Constructors / Initializers

    private KinematicSolver(int steps, CartesianJoints cartesianJoints, List<Link> links, double shockStroke)
    {
        this.steps = steps;
        this.cartesianJoints = cartesianJoints;
        this.links = links;
        this.shockStroke = shockStroke;
    }

    public static KinematicSolver Create(int steps, Linkage linkage)
    {
        var cartesianJoints = new CartesianJoints(linkage.Joints, linkage.Links, linkage.Shock);
        return new KinematicSolver(steps, cartesianJoints, linkage.Links, linkage.ShockStroke);
    }

    #endregion Constructors / Initializers
    
    #region Public methods

    public Dictionary<string, CoordinateList> SolveSuspensionMotion()
    {
        polarJoints = new PolarJoints(cartesianJoints, links);
        
        // Find the input angles for the given travel
        var inputAngles = FindInputAngleRange(shockStroke);
        var solutions = new Dictionary<string, CoordinateList>();

        var combinedJoints = new List<Joint>(cartesianJoints.KinematicLoopJoints);
        combinedJoints.AddRange(cartesianJoints.EndEffectorJoints);

        // Solve the linkage for each angle and convert to cartesian
        foreach (var t in inputAngles)
        {
            polarJoints.KinematicLoopPolar[0].Theta = t;
            var klpSol = SolveKinematicLoop();
            var solution = SolutionToCartesian(klpSol);
            for (var j = 0; j < combinedJoints.Count; ++j)
            {
                var name = combinedJoints[j].Name!;
                if (!solutions.ContainsKey(name))
                {
                    solutions.Add(name, new CoordinateList([], []));
                }

                solutions[name].X.Add(solution[j].X);
                solutions[name].Y.Add(solution[j].Y);
            }
        }

        // Add static joints to the solution as well.
        foreach (var joint in cartesianJoints.StaticJoints)
        {
            if (joint.Name is not null && !solutions.ContainsKey(joint.Name))
            {
                solutions.Add(joint.Name, new CoordinateList([joint.X], [joint.Y]));
            }
        }

        return solutions;
    }

    #endregion Public methods

    #region Private methods

    private double[] FindInputAngleRange(double stroke)
    {
        Debug.Assert(polarJoints != null);

        // Find angle that minimizes error between desired y position and the rear wheel y position
        var desiredShockLength = cartesianJoints.ShockMaxLength - stroke;
        var thIn0 = GeometryUtils.NormalizeAngle(polarJoints.KinematicLoopPolar[0].Theta);

        double? lastF = null;
        var fatol = 1e-4;
        var objective = new Func<Vector<double>, double>(x =>
        {
            var normalizedX = GeometryUtils.NormalizeVector(x).ToArray();
            var fx = TravelFindEquation(normalizedX, desiredShockLength);
            if (lastF.HasValue && Math.Abs(fx - lastF.Value) < fatol)
            {
                // Early stopping condition met. This is to simulate scipy.optimize.minimize's fatol option.
                throw new EarlyStopException(Vector<double>.Build.DenseOfArray(normalizedX));
            }

            lastF = fx;
            return fx;
        });
        var solver = new NelderMeadSimplex(1e-4, maximumIterations: 1000);
        var initialGuess = Vector<double>.Build.DenseOfArray([thIn0]);

        double thInEnd;
        try
        {
            var result = solver.FindMinimum(ObjectiveFunction.Value(objective), initialGuess);
            thInEnd = result.MinimizingPoint[0];
        }
        catch (EarlyStopException ex)
        {
            thInEnd = ex.X[0];
        }
        thInEnd = GeometryUtils.NormalizeAngle(thInEnd);

        // Create the return vector from initial and final angles
        var inputAngles = new double[steps];
        for (var i = 0; i < steps; i++)
        {
            inputAngles[i] = thIn0 + (thInEnd  - thIn0) * i / (steps - 1);
        }

        return inputAngles;
    }

    private double TravelFindEquation(double[] x, double desiredShockLength)
    {
        Debug.Assert(polarJoints != null);

        // The optimization variable is the input angle of the linkage
        polarJoints.KinematicLoopPolar[0].Theta = x[0];

        var kinematicLoopSolution = SolveKinematicLoop();
        var cartesianSolution = SolutionToCartesian(kinematicLoopSolution);

        var shockEye1 = cartesianJoints.ShockEyeTypes[0] == JointType.Fixed ?
            cartesianJoints.StaticJoints[cartesianJoints.ShockEyeIndices[0]] :
            cartesianSolution[cartesianJoints.ShockEyeIndices[0]];
        var shockEye2 = cartesianJoints.ShockEyeTypes[1] == JointType.Fixed ?
            cartesianJoints.StaticJoints[cartesianJoints.ShockEyeIndices[1]] :
            cartesianSolution[cartesianJoints.ShockEyeIndices[1]];
        var dx = shockEye1.X - shockEye2.X;
        var dy = shockEye1.Y - shockEye2.Y;
        var currentShockLength = Math.Sqrt(dx * dx + dy * dy);
        return Math.Abs(desiredShockLength - currentShockLength);
    }

    private static double ConstraintEquation(List<double> x, List<double> args)
    {
        var xv = Vector<double>.Build.DenseOfEnumerable(x);
        var argsv = Vector<double>.Build.DenseOfEnumerable(args);
        var n = xv.Count + argsv.Count;
        var q = n / 2;

        // Combine geo (args) and x into theta
        var theta = Vector<double>.Build.Dense(q);
        theta.SetSubVector(0, 1, argsv.SubVector(0, 1));
        theta.SetSubVector(1, xv.Count, xv);
        theta.SetSubVector(1 + xv.Count, q - xv.Count - 1, argsv.SubVector(1, q - xv.Count - 1));

        // Compute cos(theta) and sin(theta)
        var ctheta = theta.PointwiseCos();
        var stheta = theta.PointwiseSin();

        // Stack ctheta and stheta into a matrix
        var thetas = Matrix<double>.Build.Dense(2, q);
        thetas.SetRow(0, ctheta);
        thetas.SetRow(1, stheta);

        var l = argsv.SubVector(q - xv.Count, argsv.Count - (q - xv.Count));
        var u = thetas * l;
        return u.L2Norm();
    }

    private List<PolarCoordinate> SolveKinematicLoop()
    {
        Debug.Assert(polarJoints != null);

        // Construct input data for minimizer.
        // TODO: Needs testing for higher than 4-bar linkages.
        var x = polarJoints.KinematicLoopPolar[1..^1].Select(pc => GeometryUtils.NormalizeAngle(pc.Theta)).ToList();
        var geo = new List<double>
        {
            GeometryUtils.NormalizeAngle(polarJoints.KinematicLoopPolar[0].Theta),
            GeometryUtils.NormalizeAngle(polarJoints.KinematicLoopPolar[^1].Theta)
        };
        geo.AddRange(polarJoints.KinematicLoopPolar.Select(pc => pc.Length));

        // Minimize the objective function using the BFGS optimizer
        var objective = new Func<Vector<double>, double>(v =>
        {
            var normalized = GeometryUtils.NormalizeVector(v).ToList();
            return ConstraintEquation(normalized, geo);
        });
        var gradient = new Func<Vector<double>, Vector<double>>(v =>
        {
            var normalizedV = GeometryUtils.NormalizeVector(v);
            return NumericalGradient.ComputeGradient(y =>
            {
                var normalized = GeometryUtils.NormalizeVector(y).ToList();
                return ConstraintEquation(normalized, geo);
            }, Vector<double>.Build.DenseOfEnumerable(normalizedV));
        });
        var initialGuess = Vector<double>.Build.DenseOfEnumerable(x);
        var optimizer = new BfgsMinimizer(1e-8, 1e-8, 1e-8);
        var result = optimizer.FindMinimum(ObjectiveFunction.Gradient(objective, gradient), initialGuess);

        // Construct return List by replacing Thetas for kinematicLoopPolar[1..^1] with the minimized values.
        var solution = new List<PolarCoordinate>(polarJoints.KinematicLoopPolar);
        for (var i = 1; i < solution.Count - 1; ++i)
        {
            solution[i].Theta = GeometryUtils.NormalizeAngle(result.MinimizingPoint[i - 1]);
        }

        return solution;
    }

    private List<Joint> SolutionToCartesian(List<PolarCoordinate> kinematicLoopSolution)
    {
        Debug.Assert(polarJoints != null);

        // Linkage loop Joints can be directly converted
        var kinematicLoopCartesian = PolarToCartesian(cartesianJoints.KinematicLoopOffset, kinematicLoopSolution, true);

        // End effector Joints need to be dealt with
        var endEffectorCartesian = new List<Joint>();
        for (var i = 0; i < polarJoints.EndEffectorPolar.Count; ++i)
        {
            var eepOffset = kinematicLoopCartesian[polarJoints.EndEffectorOffsetFromIndices[i]];
            
            var eepSol = new List<PolarCoordinate> { new(
                polarJoints.KinematicLoopPolar[polarJoints.EndEffectorOffsetFromIndices[i]].Theta - polarJoints.EndEffectorPolar[i].Theta,
                polarJoints.EndEffectorPolar[i].Length)};
            var p = PolarToCartesian(eepOffset, eepSol);
            endEffectorCartesian.Add(p[1]);
        }

        return [.. kinematicLoopCartesian, .. endEffectorCartesian];
    }

    private static List<Joint> PolarToCartesian(Joint offset, List<PolarCoordinate> polar, bool isLoop = false)
    {
        var cartesian = new List<Joint>
        {
            new(offset.X, offset.Y)
        };

        var size = isLoop ? polar.Count : polar.Count + 1;

        for (var i = 0; i < size - 1; ++i)
        {
            var lcos = polar[i].Length * Math.Cos(polar[i].Theta);
            var lsin = polar[i].Length * Math.Sin(polar[i].Theta);
            cartesian.Add(new Joint(cartesian[^1].X + lcos, cartesian[^1].Y + lsin));
        }

        return cartesian;
    }

    #endregion Private methods
}
