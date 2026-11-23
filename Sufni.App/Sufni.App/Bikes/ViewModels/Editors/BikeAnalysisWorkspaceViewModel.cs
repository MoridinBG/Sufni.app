using Sufni.App.Bikes.Models;
using Sufni.Kinematics;

namespace Sufni.App.Bikes.ViewModels.Editors;

public sealed class BikeAnalysisWorkspaceViewModel(BikeEditorStateController controller) : BikeEditorWorkspace(controller)
{
    public CoordinateList? LeverageRatioData => State.Presentation?.Data.LeverageRatioData;
    public bool IsPlotBusy => State.Request.Busy;
    public bool HasRearSuspension => State.Inputs.Mode != RearSuspensionKind.None;
}
