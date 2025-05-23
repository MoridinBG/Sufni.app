namespace Sufni.Kinematics;

public class JointNameMapping
{
    public string RearWheel = "Rear wheel";
    public string FrontWheel = "Front wheel";
    public string BottomBracket = "Bottom bracket";
    public string ShockEye1 = "Shock eye 1";
    public string ShockEye2 = "Shock eye 2";
}

public class BikeCharacteristics
{
    public double? FrontStroke { get; private set; }
    public double? RearStroke { get; private set; }
    public double HeadAngle { get; private set; }
    public double? MaxFrontTravel { get; private set; }
    public double? MaxRearTravel { get; private set; }
    public CoordinateList LeverageRatioData
    {
        get
        {
            _leverageRatioData ??= CalculateLeverageRatioData();
            return _leverageRatioData.Value;
        }
    }

    private readonly Dictionary<string, CoordinateList> _solution;
    private readonly JointNameMapping _mapping;
    private CoordinateList? _leverageRatioData;

    public BikeCharacteristics(Dictionary<string, CoordinateList> solution, JointNameMapping? mapping = null, double? frontStroke = null, double headAngle = 0)
    {
        _solution = solution;
        _mapping = mapping ?? new JointNameMapping();
        FrontStroke = frontStroke;
        HeadAngle = headAngle;

        MaxFrontTravel = Math.Sin(HeadAngle * Math.PI / 180.0) * frontStroke;
        MaxRearTravel = Math.Abs(solution[_mapping.RearWheel].Y[^1] - solution[_mapping.RearWheel].Y[0]);
        var dx = solution[_mapping.ShockEye1].X[0] - solution[_mapping.ShockEye2].X[0];
        var dy = solution[_mapping.ShockEye1].Y[0] - solution[_mapping.ShockEye2].Y[0];
        RearStroke = Math.Sqrt(dx * dx + dy * dy);
    }

    public CoordinateList AngleToTravelDataset(string centralJoint, string adjacentJoint1, string adjacentJoint2)
    {
        var sensorJointMotion = _solution[centralJoint];
        var adjacentJoint1Motion = _solution[adjacentJoint1];
        var adjacentJoint2Motion = _solution[adjacentJoint2];
        List<double> angles = [];
        List<double> travel = [];
        var travel0 = _solution["Rear wheel"].Y[0];
        for (var i = 0; i < sensorJointMotion.X.Count; ++i)
        {
            angles.Add(CalculateAngle(
                sensorJointMotion.X[i], sensorJointMotion.Y[i],
                adjacentJoint1Motion.X[i], adjacentJoint1Motion.Y[i],
                adjacentJoint2Motion.X[i], adjacentJoint2Motion.Y[i]));
            travel.Add(_solution["Rear wheel"].Y[i] - travel0);
        }

        return new CoordinateList([.. angles], [.. travel]);
    }


    private CoordinateList CalculateLeverageRatioData()
    {
        // Calculate wheel travels
        var travel0 = _solution[_mapping.RearWheel].Y[0];
        var wheelTravels = _solution[_mapping.RearWheel].Y.Select(t => t - travel0).ToList();

        // Calculate shock lengths
        IEnumerable<double> dx = [];
        IEnumerable<double> dy = [];

        if (_solution[_mapping.ShockEye1].X.Count > 1 && _solution[_mapping.ShockEye2].X.Count > 1) // Both shock eyes are floating
        {
            dx = _solution[_mapping.ShockEye1].X.Zip(_solution[_mapping.ShockEye2].X, (a, b) => a - b);
            dy = _solution[_mapping.ShockEye1].Y.Zip(_solution[_mapping.ShockEye2].Y, (a, b) => a - b);
        }
        else if (_solution[_mapping.ShockEye1].X.Count > 1) // ShockEye1 is not fixed.
        {
            dx = _solution[_mapping.ShockEye1].X.Select(x => x - _solution[_mapping.ShockEye2].X[0]);
            dy = _solution[_mapping.ShockEye1].Y.Select(y => y - _solution[_mapping.ShockEye2].Y[0]);
        }
        else if (_solution[_mapping.ShockEye2].X.Count > 1) // ShockEye2 is not fixed.
        {
            dx = _solution[_mapping.ShockEye2].X.Select(x => x - _solution[_mapping.ShockEye1].X[0]);
            dy = _solution[_mapping.ShockEye2].Y.Select(y => y - _solution[_mapping.ShockEye1].Y[0]);
        }
        var shockLengths = dx.Zip(dy, (a, b) => Math.Sqrt(a * a + b * b)).ToArray();

        List<double> lr = [];
        for (var i = 1; i < wheelTravels.Count; ++i)
        {
            var wdiff = wheelTravels[i] - wheelTravels[i - 1];
            var sdiff = shockLengths[i - 1] - shockLengths[i];
            lr.Add(wdiff / sdiff);
        }

        return new CoordinateList(wheelTravels[1..], lr);
    }

    private static double CalculateAngle(double centralX, double centralY, double adjacent1X, double adjacent1Y, double adjacent2X, double adjacent2Y)
    {
        // Create vectors from central to each adjacent point
        var (x1, y1) = (adjacent1X - centralX, adjacent1Y - centralY);
        var (x2, y2) = (adjacent2X - centralX, adjacent2Y - centralY);

        // Compute the dot product and magnitudes
        var dot = x1 * x2 + y1 * y2;
        var mag1 = Math.Sqrt(x1 * x1 + y1 * y1);
        var mag2 = Math.Sqrt(x2 * x2 + y2 * y2);

        // Prevent division by zero
        if (mag1 == 0 || mag2 == 0) return 0;

        // Compute the angle in radians
        var cosTheta = Math.Clamp(dot / (mag1 * mag2), -1.0, 1.0);
        return Math.Acos(cosTheta);
    }
}
