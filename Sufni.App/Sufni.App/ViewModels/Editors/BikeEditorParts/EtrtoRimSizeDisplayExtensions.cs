using Sufni.Kinematics;

namespace Sufni.App.ViewModels.Editors.Bike;

internal static class EtrtoRimSizeDisplayExtensions
{
    extension(EtrtoRimSize rimSize)
    {
        public string DisplayName => rimSize switch
        {
            EtrtoRimSize.Inch24 => "24\" (507mm)",
            EtrtoRimSize.Inch26 => "26\" (559mm)",
            EtrtoRimSize.Inch275 => "27.5\" (584mm)",
            EtrtoRimSize.Inch29 => "29\" (622mm)",
            _ => rimSize.ToString()
        };
    }
}
