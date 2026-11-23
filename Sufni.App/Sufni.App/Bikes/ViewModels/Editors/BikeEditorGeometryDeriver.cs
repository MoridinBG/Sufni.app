using Sufni.App.Bikes.Models;

namespace Sufni.App.Bikes.ViewModels.Editors;

public static class BikeEditorGeometryDeriver
{
    public static double? PhotoHeadAngle(RearSuspensionKind mode, bool hasImage, HeadAngleGeometry geometry) =>
        mode == RearSuspensionKind.Linkage && hasImage ? BikeGeometry.HeadAngle(geometry) : null;
}
