namespace Sufni.Kinematics;

public enum EtrtoRimSize
{
    Inch24 = 507,
    Inch26 = 559,
    Inch275 = 584,
    Inch29 = 622
}

public static class EtrtoRimSizeExtensions
{
    extension(EtrtoRimSize rimSize)
    {
        public double BeadDiameterMm => (double)rimSize;

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