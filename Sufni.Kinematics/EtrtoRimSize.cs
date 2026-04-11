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

        // Tires are supposedly as high as they are wide
        // https://www.dirtmerchantbikes.com/special-events/2014/11/20/tire-comparison-test-report-2015-nobby-nic-high-roller-ii-neo-moto-hans-dampf
        public double CalculateTotalDiameterMm(double tireWidthInches)
        {
            return rimSize.BeadDiameterMm + (tireWidthInches * 2 * 25.4);
        }
    }
}