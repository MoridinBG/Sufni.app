namespace Sufni.Kinematics;

public class JointNameMapping
{
    public string RearWheel = "Rear wheel";
    public string FrontWheel = "Front wheel";
    public string BottomBracket = "Bottom bracket";
    public string ShockEye1 = "Shock eye 1";
    public string ShockEye2 = "Shock eye 2";
    public string HeadTube1 = "Head tube 1";
    public string HeadTube2 = "Head tube 2";
}

public class BikeCharacteristics
{
    #region Public properties

    public double? FrontStroke { get; private set; }
    public double? RearStroke { get; private set; }
    public double HeadAngle { get; private set; }
    public double? MaxFrontTravel { get; private set; }
    public double? MaxRearTravel { get; private set; }
    public CoordinateList LeverageRatioData
    {
        get
        {
            leverageRatioData ??= CalculateLeverageRatioData();
            return leverageRatioData.Value;
        }
    }

    #endregion Public properties

    #region Private fields

    private readonly Dictionary<string, CoordinateList> solution;
    private readonly JointNameMapping mapping;
    private CoordinateList? leverageRatioData;

    #endregion Private fields

    #region Constructors

    public BikeCharacteristics(Dictionary<string, CoordinateList> solution, JointNameMapping? mapping = null, double? frontStroke = null, double headAngle = 0)
    {
        this.solution = solution;
        this.mapping = mapping ?? new JointNameMapping();
        FrontStroke = frontStroke;
        HeadAngle = headAngle;

        MaxFrontTravel = Math.Sin(HeadAngle * Math.PI / 180.0) * frontStroke;
        var rearWheelDx = solution[this.mapping.RearWheel].X[^1] - solution[this.mapping.RearWheel].X[0];
        var rearWheelDy = solution[this.mapping.RearWheel].Y[^1] - solution[this.mapping.RearWheel].Y[0];
        MaxRearTravel = Math.Sqrt(rearWheelDx * rearWheelDx + rearWheelDy * rearWheelDy);
        var dx = solution[this.mapping.ShockEye1].X[0] - solution[this.mapping.ShockEye2].X[0];
        var dy = solution[this.mapping.ShockEye1].Y[0] - solution[this.mapping.ShockEye2].Y[0];
        RearStroke = Math.Sqrt(dx * dx + dy * dy);
    }

    #endregion Constructors

    #region Public methods

    public CoordinateList AngleToTravelDataset(string centralJoint, string adjacentJoint1, string adjacentJoint2)
    {
        var angles = CalculateAngles(centralJoint, adjacentJoint1, adjacentJoint2);
        var travel = CalculateRearWheelTravel();
        return new CoordinateList(angles, travel);
    }

    public CoordinateList AngleToShockStrokeDataset(string centralJoint, string adjacentJoint1, string adjacentJoint2)
    {
        var angles = CalculateAngles(centralJoint, adjacentJoint1, adjacentJoint2);
        var shockStroke = CalculateShockStroke();
        return new CoordinateList(angles, shockStroke);
    }

    public CoordinateList ShockStrokeToWheelTravelDataset()
    {
        var shockStroke = CalculateShockStroke();
        var travel = CalculateRearWheelTravel();
        return new CoordinateList(shockStroke, travel);
    }

    #endregion Public methods

    #region Private methods

    private CoordinateList CalculateLeverageRatioData()
    {
        var wheelTravels = CalculateRearWheelTravel();
        var shockStroke = CalculateShockStroke();

        List<double> lr = [];
        for (var i = 1; i < wheelTravels.Count; ++i)
        {
            var wdiff = wheelTravels[i] - wheelTravels[i - 1];
            var sdiff = shockStroke[i] - shockStroke[i - 1];
            lr.Add(wdiff / sdiff);
        }

        return new CoordinateList(wheelTravels[1..], lr);
    }

    private List<double> CalculateAngles(string centralJoint, string adjacentJoint1, string adjacentJoint2)
    {
        var sensorJointMotion = solution[centralJoint];
        var adjacentJoint1Motion = solution[adjacentJoint1];
        var adjacentJoint2Motion = solution[adjacentJoint2];
        List<double> angles = [];
        for (var i = 0; i < sensorJointMotion.X.Count; ++i)
        {
            angles.Add(GeometryUtils.CalculateAngleAtPoint(
                sensorJointMotion.X[i], sensorJointMotion.Y[i],
                adjacentJoint1Motion.X[i], adjacentJoint1Motion.Y[i],
                adjacentJoint2Motion.X[i], adjacentJoint2Motion.Y[i]));
        }

        return angles;
    }

    private List<double> CalculateRearWheelTravel()
    {
        var x0 = solution[mapping.RearWheel].X[0];
        var y0 = solution[mapping.RearWheel].Y[0];
        return solution[mapping.RearWheel].X
            .Zip(solution[mapping.RearWheel].Y, (x, y) =>
                Math.Sqrt((x - x0) * (x - x0) + (y - y0) * (y - y0)))
            .ToList();
    }

    private List<double> CalculateShockStroke()
    {
        IEnumerable<double> dx = [];
        IEnumerable<double> dy = [];

        if (solution[mapping.ShockEye1].X.Count > 1 && solution[mapping.ShockEye2].X.Count > 1)
        {
            dx = solution[mapping.ShockEye1].X.Zip(solution[mapping.ShockEye2].X, (a, b) => a - b);
            dy = solution[mapping.ShockEye1].Y.Zip(solution[mapping.ShockEye2].Y, (a, b) => a - b);
        }
        else if (solution[mapping.ShockEye1].X.Count > 1)
        {
            dx = solution[mapping.ShockEye1].X.Select(x => x - solution[mapping.ShockEye2].X[0]);
            dy = solution[mapping.ShockEye1].Y.Select(y => y - solution[mapping.ShockEye2].Y[0]);
        }
        else if (solution[mapping.ShockEye2].X.Count > 1)
        {
            dx = solution[mapping.ShockEye2].X.Select(x => x - solution[mapping.ShockEye1].X[0]);
            dy = solution[mapping.ShockEye2].Y.Select(y => y - solution[mapping.ShockEye1].Y[0]);
        }

        var shockLengths = dx.Zip(dy, (a, b) => Math.Sqrt(a * a + b * b)).ToArray();
        var initialShockLength = shockLengths[0];
        return shockLengths.Select(length => initialShockLength - length).ToList();
    }

    #endregion Private methods
}
