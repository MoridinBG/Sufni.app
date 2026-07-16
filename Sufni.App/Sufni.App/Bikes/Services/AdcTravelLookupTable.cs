using System;

namespace Sufni.App.Bikes.Services;

internal sealed class AdcTravelLookupTable
{
    internal const int EntryCount = 4096;

    private readonly Func<ushort, double> fallback;
    private readonly double[] values = new double[EntryCount];

    public AdcTravelLookupTable(Func<ushort, double> fallback)
    {
        this.fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));

        for (var measurement = 0; measurement < values.Length; measurement++)
        {
            values[measurement] = fallback((ushort)measurement);
        }

        MeasurementToTravel = measurement =>
            measurement < this.values.Length ? this.values[measurement] : this.fallback(measurement);
    }

    internal int Count => values.Length;

    internal long RetainedValueBytes => values.LongLength * sizeof(double);

    public Func<ushort, double> MeasurementToTravel { get; }
}
