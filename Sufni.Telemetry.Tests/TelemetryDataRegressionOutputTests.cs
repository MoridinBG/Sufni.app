using Sufni.Telemetry;

namespace Sufni.Telemetry.Tests;

// §11.1 SIMD safety net. A single deterministic V4 / 1000 Hz recording is processed
// through TelemetryData.FromRecording; every floating output is compared, per element,
// against baked literals (tolerance 1e-9), integers exactly.
//
// These literals are the accepted v4 output. The suite guards against unintended DSP drift
// while allowing implementation changes underneath the processing pipeline.
public class TelemetryDataRegressionOutputTests
{
    // Deterministic ride: compression, rebound, a deeper compression/rebound, a 220 ms
    // airtime idle at 0, and a fast landing. Calibration val/10 = mm; MaxTravel 100 mm;
    // rear mirrors front at 80% amplitude. Kept in sync with the capture that produced
    // the literals below.
    private static (RawTelemetryData Raw, Metadata Meta, BikeData Bike) BuildRecording()
    {
        const int count = 600;
        var front = new ushort[count];
        var rear = new ushort[count];

        static void Ramp(ushort[] ch, int from, int to, double startVal, double endVal)
        {
            for (var i = from; i < to; i++)
            {
                var t = (i - from) / (double)(to - from);
                ch[i] = (ushort)Math.Round(startVal + (endVal - startVal) * t);
            }
        }

        Ramp(front,   0,  70,   0, 420);   // compression 0 -> 42 mm
        Ramp(front,  70, 140, 420,   0);   // rebound 42 -> 0 mm
        Ramp(front, 140, 250,   0, 800);   // deeper compression 0 -> 80 mm
        Ramp(front, 250, 340, 800,   0);   // rebound 80 -> 0 mm
        // 340..560 airtime idle at 0 for 220 ms (>= AirtimeDurationThreshold 0.20 s)
        Ramp(front, 560, 600,   0, 900);   // landing compression 0 -> 90 mm (~2250 mm/s > 500)

        for (var i = 0; i < count; i++)
        {
            rear[i] = (ushort)Math.Round(front[i] * 0.8);
        }

        var raw = new RawTelemetryData { Version = 4, SampleRate = 1000, Front = front, Rear = rear };
        var meta = new Metadata { SampleRate = 1000 };
        var bike = new BikeData(100.0, 100.0, v => v / 10.0, v => v / 10.0);
        return (raw, meta, bike);
    }

    private static readonly TelemetryData Result = Process();

    private static TelemetryData Process()
    {
        var (raw, meta, bike) = BuildRecording();
        return TelemetryData.FromRecording(raw, meta, bike);
    }

    private static void AssertClose(double[] expected, IReadOnlyList<double> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], actual[i], 1e-9);
        }
    }

    private static void AssertClose(double[][] expected, IReadOnlyList<double[]> actual)
    {
        Assert.Equal(expected.Length, actual.Count);
        for (var i = 0; i < expected.Length; i++)
        {
            AssertClose(expected[i], actual[i]);
        }
    }

    private static int[] Bounds(Stroke[] strokes) =>
        strokes.SelectMany(s => new[] { s.Start, s.End }).ToArray();

    [Fact]
    public void Signals_MatchAcceptedV4Output()
    {
        AssertClose(DecodedBaseline.FrontTravel, Result.Front.Travel);
        AssertClose(DecodedBaseline.FrontVelocity, Result.Front.Velocity);
        AssertClose(DecodedBaseline.RearTravel, Result.Rear.Travel);
        AssertClose(DecodedBaseline.RearVelocity, Result.Rear.Velocity);
    }

    [Fact]
    public void BinsAndHistograms_MatchAcceptedV4Output()
    {
        AssertClose(DecodedBaseline.FrontTravelBins, Result.Front.TravelBins);
        AssertClose(DecodedBaseline.FrontVelocityBins, Result.Front.VelocityBins);
        AssertClose(DecodedBaseline.FrontFineVelocityBins, Result.Front.FineVelocityBins);
        AssertClose(DecodedBaseline.RearTravelBins, Result.Rear.TravelBins);
        AssertClose(DecodedBaseline.RearVelocityBins, Result.Rear.VelocityBins);
        AssertClose(DecodedBaseline.RearFineVelocityBins, Result.Rear.FineVelocityBins);
        AssertClose(DecodedBaseline.FrontTravelHistogram,
            TelemetryStatistics.CalculateTravelHistogram(Result, SuspensionType.Front).Values);
        AssertClose(DecodedBaseline.RearTravelHistogram,
            TelemetryStatistics.CalculateTravelHistogram(Result, SuspensionType.Rear).Values);
        AssertClose(DecodedBaseline.FrontVelocityHistogram,
            TelemetryStatistics.CalculateVelocityHistogram(Result, SuspensionType.Front).Values);
        AssertClose(DecodedBaseline.RearVelocityHistogram,
            TelemetryStatistics.CalculateVelocityHistogram(Result, SuspensionType.Rear).Values);
        AssertClose(DecodedBaseline.FrontTravelFrequencyHistogram,
            TelemetryStatistics.CalculateTravelFrequencyHistogram(Result, SuspensionType.Front).Values);
        AssertClose(DecodedBaseline.RearTravelFrequencyHistogram,
            TelemetryStatistics.CalculateTravelFrequencyHistogram(Result, SuspensionType.Rear).Values);
    }

    [Fact]
    public void Statistics_MatchAcceptedV4Output()
    {
        var frontTravel = TelemetryStatistics.CalculateTravelStatistics(Result, SuspensionType.Front);
        Assert.Equal(87.799999999999997, frontTravel.Max, 1e-9);
        Assert.Equal(32.468030690537084, frontTravel.Average, 1e-9);
        Assert.Equal(0, frontTravel.Bottomouts);

        var rearTravel = TelemetryStatistics.CalculateTravelStatistics(Result, SuspensionType.Rear);
        Assert.Equal(70.200000000000003, rearTravel.Max, 1e-9);
        Assert.Equal(25.974424552429667, rearTravel.Average, 1e-9);
        Assert.Equal(0, rearTravel.Bottomouts);

        var frontVelocity = TelemetryStatistics.CalculateVelocityStatistics(Result, SuspensionType.Front);
        Assert.Equal(-716.7213791917352, frontVelocity.AverageRebound, 1e-9);
        Assert.Equal(-988.49222436178911, frontVelocity.MaxRebound, 1e-9);
        Assert.Equal(919.4416384422168, frontVelocity.AverageCompression, 1e-9);
        Assert.Equal(2390.9645507471696, frontVelocity.MaxCompression, 1e-9);
        Assert.Equal(-988.49222436178911, frontVelocity.Percentile95Rebound, 1e-9);
        Assert.Equal(2390.9645507471696, frontVelocity.Percentile95Compression, 1e-9);
        Assert.Equal(2, frontVelocity.ReboundStrokeCount);
        Assert.Equal(3, frontVelocity.CompressionStrokeCount);

        var rearVelocity = TelemetryStatistics.CalculateVelocityStatistics(Result, SuspensionType.Rear);
        Assert.Equal(-573.26361756137794, rearVelocity.AverageRebound, 1e-9);
        Assert.Equal(-791.5510737249864, rearVelocity.MaxRebound, 1e-9);
        Assert.Equal(735.34452565233721, rearVelocity.AverageCompression, 1e-9);
        Assert.Equal(1912.7374075200241, rearVelocity.MaxCompression, 1e-9);
        Assert.Equal(-791.5510737249864, rearVelocity.Percentile95Rebound, 1e-9);
        Assert.Equal(1912.7374075200241, rearVelocity.Percentile95Compression, 1e-9);
        Assert.Equal(2, rearVelocity.ReboundStrokeCount);
        Assert.Equal(3, rearVelocity.CompressionStrokeCount);
    }

    [Fact]
    public void StrokesAndAirtime_MatchAcceptedV4Output()
    {
        Assert.Equal(3, Result.Front.Strokes.Compressions.Length);
        Assert.Equal(2, Result.Front.Strokes.Rebounds.Length);
        Assert.Equal(3, Result.Rear.Strokes.Compressions.Length);
        Assert.Equal(2, Result.Rear.Strokes.Rebounds.Length);
        Assert.Equal(ExpectedFrontCompressionBounds, Bounds(Result.Front.Strokes.Compressions));
        Assert.Equal(ExpectedFrontReboundBounds, Bounds(Result.Front.Strokes.Rebounds));
        Assert.Equal(ExpectedRearCompressionBounds, Bounds(Result.Rear.Strokes.Compressions));
        Assert.Equal(ExpectedRearReboundBounds, Bounds(Result.Rear.Strokes.Rebounds));
        Assert.Single(Result.Airtimes);
    }

    private sealed record RegressionBaseline(
        double[] FrontTravel,
        double[] FrontVelocity,
        double[] RearTravel,
        double[] RearVelocity,
        double[] FrontTravelBins,
        double[] FrontVelocityBins,
        double[] FrontFineVelocityBins,
        double[] RearTravelBins,
        double[] RearVelocityBins,
        double[] RearFineVelocityBins,
        double[] FrontTravelHistogram,
        double[] RearTravelHistogram,
        double[][] FrontVelocityHistogram,
        double[][] RearVelocityHistogram,
        double[] FrontTravelFrequencyHistogram,
        double[] RearTravelFrequencyHistogram);

    private const string ExpectedBaselineBase64 =
        """
        H4sIAAAAAAAC/+18d1xUybI/UTERDAiIigIGJCcVUbrIIDkKoiIiCioZBBEkDqhgwoCKophddc1r5pgwoZgVFSNiWLMoBtQf2tUzvzn7uHs33PfefZfzz/dT
        p6u/XV1d1adnTs0Mk5IQXiY/rgdWFN9ana34fjWgLE2olhyh99siKhLa3oEsWfz96oR6KiTsx6WGeuqo1x1lDdTrifc1UV8LebRRrxfq9UG9vqing3r9UE8X
        9fRRzwDRENsNsZ8R6hljf2PUM0E9E9QzRT0z1DNDPXPUM0e9/qg3APUGoN5A1BuIehaoNwj1BqGeJepZot5g1BuCekNQzwr1rFAmVC+d4H1C9VUBeYDq7QDk
        s6Z6LtbIa031aq2Rzwb5bJDPBvlskc8W+WyRz04os3amz/ozPsbPxmPjM3uYfcxeZj+bD5sfmy+bv7g/rIT+Yv5j/mT+Zf5m/mfrwdaHrRdbP7aebH3ZerP1
        Z/HA4oPFC4sfFk8svli8sfhj8cjik8Uri18Wzyy+Wbyz+Gf5wPKD5QvLH5ZPLL9YvrH8E89HdWG+svxl+czym+U7y3+2H7D9QXy/eGvF9hO2v1D+WpQ/WFF+
        SezfUshL7yuhPR2xvTOOp4ZyV5Q1UK8HyqJ9hPJpo35vlNk+Ito/aD89lPVRxnXYYYhxZ4T9cN1q2f4h2i9ovJrhOBgP6bhfuIj2CcqLcZXO4s0CeS2E8Ul5
        MV5VWRzj/pA+BHmtkBfzPwDzhBPtBz/o41geoWyC+Zcv2gcoDebpWsxfCczrAMx7juU55n+cHfKibGKPvPbIa4+8DsjrgLyOyOuIvCirOiGvE/KibOKMvM7I
        64y8Q5F3KPK6IK8L8qKs6oq8rsiLsokb8rohrxvyuiOvO/J6UD1dDyoHo5zvQfn3emB/lFU9qWzvieN60n4rUL6EsoQX8nohL8r5Xsjrhbwoq3ojrzfyeiMv
        ypdQlvCheuXeyIft3t7iPLU43iYv5MNxBnoJ7aM8nsiD9nt7is+zFv2xyQN50A8DPcT9l++O/Zh/3cTXoXFd6Pq5Iq8L8rkI15fyDEUetv7O4nHSGDeUxwl5
        HJHHURh/lMcBeVh82ovHcWNcUx475MHnYJytMD8oD3tesvyxFs+zCnyeBeDzrRafY3FEPH9V2fOM5fdg8X2AnRt2WIifL2oHiJ9DVPuLn1dczMT3qcX4XNph
        jDxGyGMovv+Z6GM/9vzR4e2jvbFdG9s1sb0Hb39Wx/sq2K+TcJ8X3/dlhc8Ris+sJJqv5qv5ar6ar+br//DFPg9RqT0+J7sIP7fR5yj7PqQfPi8NhJ8nqb6p
        +OeOigHCz7m0nX1+sBKeQ9jnb3Z+occR9rneDvXtcTwH4Xmd9nfC/uw8huc0gue3MDxPp7vjOQTPg3c9xM/FEqJzLz024Hl1B55niQ/OxwfPyb60/a0vGSYl
        IfHxW+NVlknqET8hfkP8ypMlf8zhtyiFKN2EzFCmifsMZZvQ48v88ZvileDpSf+O3dJN6Evx7GPtzD8yTcxDmqcnwbvfAlGONw9Z3riSvPsMPyMv329RV8Mm
        XQ3LIkf7HTjSzzKbVE8OOjDxQTaxcnmtaauaQwYuf7K047Zs4qT0JiJ2XhYxhUkOy3ZnkPUq1+3qhqaRiv1Df1X9dTLReXWvw8O8aHLr49Nu4Q+DMaasBl+n
        MteftnOnqT63hvbnzCgf50L5uSF0PA7H5+5Qe7gj1D4uhtrLof0czodDvwnvt0REv3GtEDE+hYh+5tB/XGtESR6/FE9mvBI8Xj6fBA+leMjGkeHx8u1n88N4
        4OR4erI8Pv64kr8z/u8h03cZb7DyYHwWt0y5zP5weDa36u2VwRd75nAQeXCQe0AOd6K3afGX29mcytUPAtMNWZx0ZofPY5ZkcCdkZJbcSEvl9nt0Ol0TmsjF
        C9QLpYsmcv47JxWeivfl8tZpzpnzzZ30big8tDI3guisuVh+0CSRmJafUHAel0a+rG6Q9j6cSbhXY4K05+SQ67ZOj98tzyWjT+t7P7ibRyQ2LLydETidFGsl
        WW5Vnk66jXqfpTgij5Bqkx0/x+eSLMf+g913CEiDYenX6kbkLKpKjQ8LyLLST7sfNuLRSI/Rh3cJyEHEIry/D/XqsV8q8ryKl2n/pkxApvJ4D6P+Uux/BPkY
        svHY+F+xXzbyvEfeDJS/YPsh1F+C/ct5vGy8o6j3Cfuxedcjr4DHe5BnL5v/Pp4f9qLeR+yXhjwvkDcF5W9N2MvsLOf5gfnrA8+/n5A3h+ff46hfyPMDx/ND
        GepJG9F+bN51yFuAMvP/ftRfwfPD/ibi4TP2m8aLh2yeH7ahfgn2P4d8xxDX4f3jPD8w++7MK3Ab2xjn7SPWxHc5kEcWWs+eMLNkOpFrmPfo4qvppMuKYHtw
        n05aS80PO3M4l0Q8jFl8yyqHjFzIvfC5mkG6e70+M6lDClGXLzl0QDuGfPn27pyhVQB5I1dUknDam7OOyvQ76xnNSbWvlM9xSeFUQ0bvDVyayZELFgplzwRc
        B5+FChNCZnDKuTkpd2/nc2GGGruX+cziIjO8O7x6MIu7nnet1nL5LG7jvZYt1A8WcA89bMbsWpPPXXgLiVcvzuSmVMdfc78uwuN4v854Xy/rEzO57tzic+47
        Z3K6k58mz94+k1NH+Q22n0H9JOw/A5HxP0O9btjPDnn0UG5tQtsfof5M7F+AeAvvt0M9A+w3EHlMUO6A7dWoz/ovQGT326CeEfZz5PF8Q3tvo/4i7D8L8SLe
        /4h6WtivH/Iwf73C9tOon4b9GVby/Mz8qo88aih/4Pk5HfunIp7h2cPGN0Ie5nfWztYllWcP4znlfeJDiG0+9+zu2hXRF/K5jhcMWvZwLeCG7p31xHpyAXfk
        2t1xOvoF3Nq9sjoPR+Zzu2IH7vGVblyfK1suuWlM5/au71u55aiAi39Q9GiKVDY39O7W4k/v0znrH/akcNvKLozZOCWB0xiS06DeIYrz1R1dvNwylJtnY9hN
        jvPlWgRMemRTYsG12J+UdDXDiBx0iWifHWlPLs5z/9nIwIWMX+57WWWDE9Fqd1Gj3UUg000ds0wd2WeA5qv5ar6ar+ar+fqvr6tzT5o86mrDjd0/ulyxvx83
        N3X25356wVyffXMqtPeM5k6dzzF40m4E9/NQf6+oPY7cr0svH72W6EY2zOrof+ZxBDniOqnCLC2ZyF67dr1mfRY5oX1smYnZTNI9vHLqhHlzierzrkZhBQuI
        hfW7xbv9FpNZm2ryY08Wk3ByYNbBEyVk9eZXK7TUVhKtumSS+GElWao/1Slqaim5EV9U9XxzKXlZv6fs4sJScmH1mFauDqUkqGdl8eS7K4n5D/6VpOP3Scxf
        Sd4tNMyav2Al6YQya+/Ea1fmtSs30W7G43/L4w/rFBwY2YjFOtzOW434xEpDYWhju4VvevjeRsyLuHu098KV5PI00m1hI2ouWJEks2glSdgocSm2EQ+XBevd
        a8Q2V7hc96KVZPhTjfsHG3HNt3RL3cUrf3yHJX7dxfder6zo92jvEb8K67FY3QTFNvh9mzzKSsK6Dfa+jdVRsPdwFEX1WRS7IQ+rD+mBKKrTosjqSrTx7MG+
        l+wtrEehyOpT2HtDhqK6LYp6iPrC7zXp94Wi+i0qGwnfV7J6DPYek9XZUNlE+H0o5RPVZ7B6HSqz+h1ztBPrNNJF9RpUZt+jsnou9t5VVN/F6jeoPAjtRHQR
        1Xmx97qsnoO976V2i+q9qIx1TapY7+SCmC+q+xKTXYhQn/UX5xsi/n65YjDPHkuevYOE8xGfnwVv/gOF/hH31wCeP/sL/S3ufzPe+pgK1098PU146y16fy0e
        H4bC+BGLpwp9Xrzp8uJRRxiv4vHbhxffvYTxL54Pmrx86SHMJ5Zf4vmmxnsv3pmXrx2E+Sye3214+S9N2P7A9gu2f4jvJ03XedJ+UkJeqt0WeRUR26O9oros
        6l9V1Gd1ZKJ9hNVnUT1Wf8bq0bR5/mT1BH1Qvy9vXXRRT49XnyDaL1gdw5+t96TxKtofaLyJ6rhov/7YT7QvUH5RPRfVs+DlD9Ydpovquij/YF5esvpFUX2X
        WH4vJsL3N/S1kKjOi36nivVebf983Ser+6L1LFj38hbrXgh7L2QnXiejinVfYVhHswPrvyQc0A4H8bqbKqy76SWqz6H9WJ0O1oG5YD3PYifheydW70PtQOSw
        /qctvo8KENWH0X5DsR++p0rHeqIKUZ0YtR/rw3YgvnXFeYvqxaj9buL1TGHu2E9UN8bqyOg83IV1ZoyH8dL7OE462tHLVWgfq4Ni79noOKJ5snnT++gHF6yL
        eivyHx1PVBdFx3MUrgNbF3pfVB9F/StaXzoeW2eMAxc7YZywuKH3Ua61EdZLsfij42H9YTp7D2ktjGMW12L1jDt49Y7C/BDVAbPnG8szlnfsucbei9J+FsL8
        ZfVU1D5R3S/bB9i+QO+Lnle0n+g5RccTPZ/oeEbi+1SYqM6X1VmJv9/ti3yi+l723GH7KNtXxc9r3YV1V2x/pvrKyN9RWM/L9nm277P30OLnztrmOqzmq/lq
        vpqv5uv/9CVef9xW+DsY9rmN1WOx39mw3+Gwz5Psdzzscwf7nME+54qdJ0wGip9DKix5v/sS/Y6D1Wmx8zs7r7PfYdF+onotOg47d+O52gTPgarOwrotdi4W
        O2fuwDr8xaL6LdqfnWdFv3Ngvwv4/h3W9P2Balu2p5Jemww2Fx9OJSfqW1oOqEwlMcsflKffTCXdVxxxTaxNJecbVp9XeZVK8nfOcB//IZU4nYg7HfotlbQz
        CwZ52TRy/Yv7ztBWaWRDDxvN8W3TSPqKgbNU5NNI0BSTdwmNOAJx9dsJOyZIpJGOWoe3WN5PJWtR5uut4+mtR3kUtg/n8XVGvXU8PYZreXysXyC2j2xiXKYX
        gu0MS/C+EuqtQXk0tgfy7GuPept482XjrsT7nVBvBcp+PL01PPuW8/iCEaUCOpelKE8jl9qsT36ul05W18SELtqUTlJfbLiz73o6CdoJ6xdOTyfrT5193t91
        GlHVqPyyznkqOfByStWa3klkSoRU2syOcSQ/ISFVIjaCzJx7Ti5Fc7iwDmsdlTls5xKoPneI9uc6Uz5uE+XnRtDxuDQ6Pof2cFeofVxbai83ltrP4Xw5nC+H
        /uM2ooxxwg1DRH9xiqi3DGVcfyGi/7m2qLcBZfQbF8IbF9dXKGPccehvDuOK6456uB6cH28eGL9cB9Rbz7OPjY/xwqnx5huA7QxX8vzC+jG7MP64Up5fVvD0
        GHJT1/dyNp7GnQ+9f2qpazrXt5/1F/XH6ZzCU88zu+QzuKPcKyndn9O5BQPqlEJHTOPuWZ0YrSwzlRtar3V0btRk7pG+UV+7SzHch8/lvv1/CeMEZSeMb7by
        5PqVFK6f+5MT6b5dOqJQYyxpNbFT3MNlMaTPhSc1+42TyIWlkoKqu6nka0E3b98hGWR26Z31NTczyU/uKUdP380iZoP0+8YMySYFD4dIOShmE6PDo7jirCwy
        zVmpfFpZJol4Ue3avXcmMd+5/faoPpnETT5x2HvzTKLsorW/akAmUU1fmjxeL5N0QlTE+06op4/9xiLPM9m4/Dm9RLIZj7cTj1fIj/eZHrMnHHleIy/fXg/U
        78jj7cL48b4n6pliP8bzBnnHo8za3VG/PfZXRr7OiPJ43xn1dLHfOOR5jrxjULbAdleevWo8P3Tg2cv6hSHPS+SNRtmQ51+2PsxONR6vM8+/ETz/hvPsZX7o
        jP3bIJ8CLx7ceP4djTxPm1g3F9Tvwlu3brx4YOtrjP0mIs+gyFI9t8Y47xV3OfjjkSyyJPaOiVduNtH0UxhvVp5NroXWpRZDNtkjaXCj7/tMEtVSov1OiQxS
        8E1/UGD2VCI1Jj45amUC8XuiLzvEeAJR6Cazus0hb/IwUEXiyq/unIog87B+fTj36OSjl1qHE7izb16Mf1WWyo1++mvxDIdMTnZI9cVMpRzu6LVyww/yuVyr
        ep0ibbs8TtrHf/H4A3ncmsrSjC+ZedyzOTuLpU/lctPtZyRqvBZwhn4m583tBdze3UkW/YcIuEOIenjf5nbglRBLAden7aHpffUEnJVSom4bLQGnw2Rs74v6
        +3g8pnjfGfUMsZ8z8gxE2RfbrVD/GPY/iWiB931QbxD2s0Mea5S9sb0/6h/G/sd4PH6o58j6Ic8AlIdhuyXPnuOIOnjfCfX0sZ818hgz+7DdgOefXxCZ34Dp
        Yb9ByMNktg66PJ6diP3wviPPz4Ob4GH2/8Kzh/GMuHnVatcyAdc2xW//89a53PbCeE2HmFzumFXv/ZuXNcbXy3VhISNzuSs6XRR/3iHg1H7uenbrkhxuUe/C
        buWq2Vz34ZsaOlllcru2pxqUyqRz+t3vnDveNYUb+sOeRM7yY7FtgWUM57slfpHUnfEcedbFLUs9mFNO1r2Tp+nZ+NwN/ulstCkXq/Puo8LIfsSp7YWOH7tb
        E7eBhco2cg7Et/fjqLJLtiT/ZjsTwZ5BxNXu2/IlPn2a67Car+ar+Wq+mq9/eMX/qNu14lR+1PF6cv4/6noDuLIfdb5BXGHEW7d6F38uf/m2Fvv22XAjHhx2
        hXuO5PmuyfqaxWOJU5h894BR8WRbZ72Q9bHTiNf9cfM/nMqhv8/qnk+uyFfskh40lyjti3/kazOfTAqz3NNXbhGxamW0JGXiYtLi9dx374YtJZ0LzM5fOVhM
        zpq/jK+cv4x4fKxTe3t/GWkdaFcwd98y8r7ROo3BiMuKSY/vxmsUk56IGjyZjz14qNmEHuPRQtTm9WN6vRHpPIvp7/z+P/yC+B6xjocfEJ81gZ1+s1Id8Zmu
        JXxPR9FUvN5FAv93KJ19T2cr/B6OvcemMvufEvbeF38PmY6/g+TY/27g7xyJL7b7Ybs/tgdQX32/xs/hfuDWAopzZlCMzqXokU3RMIOixlSKdxMokiiKXAiH
        tnJiNnMhaEsUwX4EeQjyEhyH4LgE7SBoF0E7KX6YSzGvkKLKAorrF1IcUETx5GKKjXH6Ax8XU0xaTlFmBcUKxPkrKQaVUtRaRWqYj3Rn0zntnEXREvEY+swF
        8XI+xSDEmpkUJyLWoW+nIsog5k+n2BGxOI+iFuImXAtTxAMCiraIFTkUfRBv4ZqFIj7LohiP2IC/nctGbIs4H9dYHXF1OkVdxPHTKCalUcxLpViEMbE+heKe
        KRRPJlO8nkTx8WSKHxIpyiGqYCz1iac4II6iYyzFYTE4fjTFV5EUFSdhbE7AWA3H2B2HsTwWY3sMxfOjMXZHYSwHUQweRnGFD7Z7YGw7Y2xbU0w3x9juxhDz
        C+v/OKw7IPj9+F3MzxXsd8fDMPaDsH0UQbsw1sdg7I/FXBiHuRGOuTKB4LwJ+gFzIxpjPYag3zAX4gj6FXMFc08uEXMJ8fFkguuEuZNMcB0xt1IIrjPmXirm
        UhqOP41gnBCMG4JxhLmF2DaTYNwRjEOCcUkwTgnGLcE4JhjXmKuItgKCeUAwLwjmCeZuHsE8IphXBPMMcx/3lqmIdYgTZxLMW9wL8gnmNcE8J5j3BPcBgvsC
        zn82zh9RHvcsZ0QB4hHEr4gWuKclIm5HfIGoM49iGGIp4m1ENdwL/RDnIZ5DbD2foj1iJuIhxE+I5riXxiJuQXyK2Av32BDE5Yg3EJUXUfRCnIV4GrEF7s3W
        iGmI+xDfIxrj3h2JuBGxFrHnEoojEZcgXkVsj3u+G+IMxHJEKXwWDEGcgrgb8Q2i/jKKEYhrEe8jdsNnSSDiQsSLiPIluP6IAsQjiF8Ru+IzaBBiAOJkxAWI
        OxAvIL5EbIfPrn6ITojjEHMQVyEeRryD+AWxCz77BiL6IyYgFiJuQ6ws/bc683RjJv4HnXF+ES5L89mi+WzRfLZoPls0ny3+Xc4W339Ppnux1YmtX7VJ/Eft
        GT/t7k769/t+oxupPx1mZ3FPg1Q9sJR1HKnxm/sfg9Km39/YlTjvNWm5KkMZ9RTJ/lk6rb6ubov68k0ivMmUM1vcmhz3vdlyYsFjK9r/7m/qeL/bKK1lkOdR
        2JdUque3uJGgRbaahZQ/eqMplOPGydtLP+9JXkjvtq/4oEWUt8/J0Q3tQXzSa2SVItr/Bue6DRC8i+ogRGYTs4Hi+X+6pvj790at/8L3bXS8V78Z7zsn9WeD
        FfVzC8JH6j+539xvyp+theOdt/pzdp7/LznpWtT9po2t75/hpOslheupJIwxFjt/hrMpv/xZn/wjzt/j/n07z/+tnL+3Fv+dc2+q/a/M/X9mje7+rf78szn4
        V+z8O+Pzr+QR24//znxnMc+eSwz5zys+NpUrIs4Pf3iOTfX7K3vyX4mlP5/v//vt/E+ee7Od/9kxz+z9PfwznGxfFD+3vxLK7BzI7vPPtr/l/OPnbf7V9Q8+
        j5s6bze99hJNPicY0nnLkN+by585N/zznIqk3a93BIdSOgmxqefYP8/56r/lrPxnzxP/Ks4/6rd/hZ1/ZW/63+bPfx3n33sGzf1xdf6DeXT3H9rJzq/sXNsU
        shz+K3P/o/Y1Pzub7fx3sfPfbe4s99iZiH3+a0r+R59j/+jZ5X9i7qcQSb/7FrXWW/L7z71PFFYaCnQKpUBLaWRW7ycd4QC5Wbt7SU+YZ7u0WF9ND6qlx50d
        8NkUEssGyy9abAntJg5+82WnNdQ9Nff9tNsB+oytrnTzdgUtRxcdteOesLihUHWPni9EDynVVXswDB5JWsROqwkCjyu1E0YWBUOysfIa5QchUHr5vKOn51i4
        dS573U/3xsHpYdtJzLwI2PZxWerC0ZPgZrHq0MBe0dB9UeW4rPUxEJf8uWSnWRxY33dcrlsRD89KKk9+Dk+Edd0yXVq0S4JXcuO6tdyRDFqhIW8LA1LgqHVu
        355fp8JPstWL2i5Pgy63Nqu6dkmHG88e7cm8nw5vxx/tZb0qAzQXrRnTJygTOI/Dd663yYJB07d8kdyWBZEZ8vNHO2eDwaEgv1NXs6G7ZnlbTd8caLHFKiv+
        WA5cLuuh+kBTAFVXrG/tjhXAnq216iO2CkBF71RExC0BKGjJvlz6XgA3FTPUrn8VQJHM6dX67wTw0919N69VCaCzy3VHk80CSLl7fNfxKAF0i8pcZt5TAJMG
        tVV3PZ4DYTPzYp4F5ECnk88MFe9lw5YJ/qYnh2fDcqUtsaZnsiCk1eqL6YZZsGLcB+V7+ZnQujDja/69DHjWPnvrAYMMGPX80Yw1SenwrW3UyRdTpsHUXxWW
        7/yaCuE+gyur0qbClWTpUYIvU6Bm6/ynZ6YmQ+j2mKw1XyfDtaSylA/ZiRBQc01qoVICTIpdNG7w2jgY0Or4ueW2sXDj0JReI19Ew/q9Vf11naNA6/mnLbsu
        TwT7Eetzk6ZFQN6sqe6zXcdDC/PjZYrWYTA+e7KqQnAozDpurb1gYwg49hq5YYXWaFjk+DDNtHQUVGxJ7b5g7AiwcLittaRoOETNjukjSQLBY5Tl5l9HBUD5
        xUspez8Pg4eVj/vd6xQAOn2LO578OQAK1w/5vORUIEwuLnrWJyQIvN4O3n04cST0qszem58RDE+cFzsslgiBrumHytftHAN7pytff180FrxqdPbXrBsH11wl
        L7y/EQ6WhTlLa3Ua7R8QO3Tn7EjYv2jMmFHp0dBliW189L4YeP5lwUmn17GgsCXxcyuNeJjw8kFZOSTAYNPhvVZ5JoLtkQSu1nky9OGM1Dv1TYKPWUFyso+T
        oHBtK4X6rGQYeC963saPyaAk06kh2n4K3Op+MGHI+Ckwtj7u519GTIGOB/rbWfaaAtNvPg9/fjgZVlmoFm41SoY2Sw0/6k1Ogv7b1tw4PX8yHLKVnFlSkAiZ
        AwziwscmQNzCcJvkRnvUb4/YtuxoLGi/ijBr4xMDi2a8XZ65JQoe26y6dXPNRHhyaaVdyeJwuJIwco3+gjBw3ybrOH/VGPDLrdvtWBkM613fzay3CYKWisoL
        jjn6Qx+TBfEdDniBedSH3md8XMHEzr+20sIevn5coF7Yn4B/i4Vec1abg61UzY39BvrQ9ucpF4+masKVmr2goqAG4H7ntU/nTpB57pGMakRnUC8pf5TeVQPs
        s/zsSj16g/SUGoW8cYagO2CLzCs5c7i00aa6s4MlPFY519IwEqCo9WH34CO2MKP8+a2D3xwgK+tszTC5oZB/e92osMOuMOZ+VtCLVh6QfcHsZvJ7D5jau7WD
        dYknfOto2GL1N0/YaWhYkdTJC4zDdkd3uOcJEvd0p+qM9YRhKU6229d6wLsDb066B7hDjpH/zGXfXGCD0jvBcR9nqDxjNTszygFmO9672Xm8Leg8Wn1xlj9A
        oJPMhYebLWFS7jGfnl/Mwb2uoDTjtBGcjfxa/ihNB0aGxqZ8/tYTNl0pbnF/rRqsGrR3lKpLBwj4svXal76KcOHd5k2RgUqQF3zHddxVZZjhHVLlNEgD3Dq0
        WxD+qRf07t8i1HWyASiFrVwkt88UBlbu9lf0sQCz+71nLDUkUJ+68FN9oA2UXAyMTJpnDxN1jqwEzgm0jC5tfHLWBd5XmJ9XXesO4yZeaVGo5glB24fJnjH1
        grOvv953VfMGz6+dxlsc9wazbTfOBRj5wLuj+5b0D/KBhruDtXTtfCChaPvxgOfeACqRH4v9vUHCT61LdJ4XXL095trRHE8wzDu65qyfBxjPOFS1tKUbWCSF
        eJX0GwrfnyXfy55nX75v4a9dbeNx7BoxkBpy/Fx5PbE2cEyAY61h3TW11MnnO4OD476bNit6gkOYtFTwTR1QbOBS37kaw6dqQ5vOb/qD2uR3kzsMHwJSgofP
        K49Zw9ow+XlSZfYAbTtINHQZCtJ1ToMG3HSHqknVKndTveB5D+OprYf4grbK0IPt9IbBl8SS/Gyn4eCX3Ob2+rkjwS7O5f0aydGQ5FBx8NHZEBj9eoez4qZQ
        COSGSwUuCQPp0gejjiwcD2u31li0LomArrac8S9bJ0K10ToX5VORsPOzem1JbDT0DrmtKZ8RA7aSe5LMZsXC8IYXWaFFcTD3rqBf9bJ4WLE6xK+qOAH2jnZu
        N6cwEWT6lT+zSJ8MxSb7z3UZnQSPHg2ak2maDCbaGlcvfkgG9aqr26ZvmgJzLuW2tHFLAcmizosO3UqBVfbvZtT4TQVVOBNzav9UsKjUm7mtdSokTejj3cEm
        tTEGGywXh6bCA7/QeT2jUkHzXY/YoyGpMGmW84ujVqkwsc1yj+UtUsEw59c5Db9MBV+NFgq7fKbCwzdVa8urU+D5itUDfXxSQF225ZmSvVPgmmyst6zCFBjl
        bznhnW8y1B4LffNydhJ0WFl0Pe3QZPgWk9Pb6EEi7N50LnLhtwRI/my72aJTAnTVdjtXqh0PlzVfVDgZx8Gbfodyl1vGwpynAmcp+xhYIzWqzUn3aKhK3rp6
        /O5IUPH0Svp8cWJjrJjKFNdHQNlwueuCXuGg18ta9eHocXBWqcLp/E9jIWNfpZJfi1BYkL6xLCUuBPTnGBrDu2BwDZ5x2q7LKNil2LPMLDUIXpUaFJKOgbCw
        w3GZoTX+oBiq3qfva184MuN8XeEQH/BxP5Da5bwXhGZu/9JpgyfURBq3zK/wgB7+Fo49B3tAtcuIx1Kf3EGxu530wXYesM37zuyB0R7Axe/bXKjVGOtadZpy
        Ol6gpvM06XaaNxzJjDrop+ELNzyiU5a29oeMPfeW7BkQAAWjG94vWj0c4iz3DZZxGAlF7/Ptq2YGQ2n1jRkbWoRA1pj6iZdnjYGXZRXml7uNhYVBi7ef2xoG
        Odoj6jcOGQ8eFqnP+x4Jh5ENz5UlzSbAvj2pivVzJkJkgqWB5PlJUBDUfdboZ5HgOzLqyrG7UTBq+/1pBj7RYCZ7ziCrJBouvc9QXns+Glpahb93rI2G9F9P
        5hXciYaVlRra7Q5Ew+1DC2WrU6JhTK8FDbO7R8OdpQe+RnhEwd6dxnU/6UeCyftdtderJ0I390q9/SETYP6dyyuvN9pzoJP5uGEy42GHwoZ7D/TCQDniRWq2
        fSgUBY9RmeYbAhFyplV6IcEQMNfw+vZHQbDzor+ktmYAKJXukVLN9gUj9bcX65S9oP0MlRH7zrrCh31r53/85AA+8hH1649bg92IL5394i0hVH7Vky5HTEH/
        oE3O7s+6EKdnl7fHUxOObyqeF/NGBRLyvtnN3KQIn4I6WMo3tIaInc8k29S1g6vfTN4eWNYRVksmtja/3BWqbswuMfuiBYttXntfN9KFNuRw0tzOxpAz/dfq
        BldzKB68y2zNHAvwPCFrrr57CDybviNTzgXAv152uHprG+gt6Bk+U2AL/huS8zx22YFeUGHfEQX28PinoSqH5RxA+eGwE/u1HODIyxFfd9+zh7q6duP62djD
        SyWBYrS7HSx6tXpuokJj//b76xNmWUO61ZGeuVUE7EpGakz4OhhK5yw7qNDNArz3t5RvaWwO98n7HSMDjcGhonrE6Il68HxOtGnM8V5w0LXjm3d1GhC95n25
        kpcKXDkVN1xFQQk8VqvtXbRTDuq6tRyutF0GkhQ6Nh4XWsCgCHnH5Zbt4PpT9VbGJzrC/pc7eq4L7wrnr4U9dtfUAsuybx5xuTrQNrG0ZViVIVxy29Ti0B5T
        GPw83/HB8QHw5ml94o0HljBsTXT4tikE3AquetdGWoPvyXXmNcQW7OoDAzvW2IGd3ue9jq4OsKNoTF3LOEfw/do354yrE1hF79+qf90JJL8EyLVTcAbJ4KFP
        Z792As8Oh2p0MpygSPJMSO5hR6jdvEsQvMcBBG1ycz0m2UPo5rbuc5/YgpKnzaTt/W1g1I6bB+zHAPw/C1FCzkh+AAA=
        """;

    private static readonly RegressionBaseline DecodedBaseline = DecodeBaseline();

    private static RegressionBaseline DecodeBaseline()
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(ExpectedBaselineBase64));
        using var gzip = new System.IO.Compression.GZipStream(
            compressed,
            System.IO.Compression.CompressionMode.Decompress);
        using var reader = new BinaryReader(gzip);

        double[] ReadArray()
        {
            var values = new double[reader.ReadInt32()];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = reader.ReadDouble();
            }
            return values;
        }

        double[][] ReadJaggedArray()
        {
            var values = new double[reader.ReadInt32()][];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = ReadArray();
            }
            return values;
        }

        return new RegressionBaseline(
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadArray(),
            ReadJaggedArray(),
            ReadJaggedArray(),
            ReadArray(),
            ReadArray());
    }

    // Accepted v4 stroke-bound literals remain readable; floating arrays are encoded above.
    private static readonly int[] ExpectedFrontCompressionBounds = [0, 70, 140, 249, 555, 599];
    private static readonly int[] ExpectedFrontReboundBounds = [71, 139, 250, 345];
    private static readonly int[] ExpectedRearCompressionBounds = [0, 70, 140, 249, 555, 599];
    private static readonly int[] ExpectedRearReboundBounds = [71, 139, 250, 345];

}
