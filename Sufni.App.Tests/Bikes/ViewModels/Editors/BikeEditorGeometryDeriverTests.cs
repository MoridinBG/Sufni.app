using Avalonia;
using Sufni.App.Bikes.Coordinators;
using Sufni.App.Bikes.Models;
using Sufni.App.Bikes.ViewModels.Editors;
using Sufni.App.Bikes.ViewModels.LinkageEditing;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

public class BikeEditorGeometryDeriverTests
{
    [Theory]
    [InlineData(RearSuspensionKind.Linkage, true, 71.6, true)]
    [InlineData(RearSuspensionKind.Linkage, false, 65, false)]
    [InlineData(RearSuspensionKind.LeverageRatio, true, 65, false)]
    public void PhotoDefinesTheHeadAngleOnlyForALinkagePhoto(RearSuspensionKind mode, bool hasImage, double expected, bool derived)
    {
        var spec = TestSnapshots.FullSuspensionLinkageSpec(true);
        var inputs = new BikeEditorInputs
        {
            Mode = mode, ManualHeadAngle = 65, Chainstay = BikeGeometry.Chainstay(new RearSuspensionSpec.Linkage(spec)),
            FrontWheel = new(702, null, null), RearWheel = new(700, null, null),
            Linkage = LinkageSpecMapping.FromSpec(spec, 50, 2),
            Image = hasImage ? new BikeEditorImage(new byte[] { 1 }, new Size(200, 50)) : null
        };

        var geometry = BikeEditorGeometryDeriver.Derive(inputs, BikeDraft.From(TestSnapshots.Bike()), null);

        Assert.Equal(expected, geometry.EffectiveHeadAngle);
        Assert.Equal(derived, geometry.IsHeadAngleDerived);
    }
}
