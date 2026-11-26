using Sufni.App.Sessions.Models;
using Sufni.App.Sessions.Store;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Sessions.Models;

public class SessionTuneTests
{
    private static readonly SessionTune Empty = SessionTune.From(TestSnapshots.Session());

    [Theory]
    [InlineData(" 450 lb ", "450 lb")]
    [InlineData("   ", null)]
    [InlineData("", null)]
    public void FromTrimsTheSpringTextAndLeavesAnEmptyTextUnrecorded(string text, string? recorded)
    {
        var tune = SessionTune.From(TestSnapshots.Session() with { FrontSpringRate = text });

        Assert.Equal(recorded, tune.ValueOf(TuneField.FrontSpringRate));
    }

    [Fact]
    public void CompareGivesTheChangedFieldsInFieldOrderWithBothValues()
    {
        var current = Tune(session => session with { RearLowSpeedRebound = 8, FrontSpringRate = "80 psi", FrontHighSpeedCompression = 3 });
        var matched = Tune(session => session with { RearLowSpeedRebound = 10, FrontSpringRate = "75 psi", FrontHighSpeedCompression = 3 });

        var comparison = SessionTune.Compare(current, matched);

        Assert.Equal([new TuneChange(TuneField.FrontSpringRate, "80 psi", "75 psi"), new(TuneField.RearLowSpeedRebound, "8", "10")],
            comparison.Changes);
        Assert.Equal(1, comparison.Same);
    }

    [Fact]
    public void FieldRecordedOnOnlyOneRideIsNotRecordedAndNotAChange()
    {
        var comparison = SessionTune.Compare(Tune(session => session with { RearHighSpeedCompression = 4 }), Empty);

        Assert.Empty(comparison.Changes);
        Assert.Equal(10, comparison.NotRecorded);
    }

    [Fact]
    public void SpringTextsThatDifferOnlyInCaseOrOuterSpacesAreTheSame()
    {
        var comparison = SessionTune.Compare(Tune(session => session with { RearSpringRate = " 450 LB" }),
            Tune(session => session with { RearSpringRate = "450 lb " }));

        Assert.Empty(comparison.Changes);
        Assert.Equal(1, comparison.Same);
    }

    [Fact]
    public void ChangesSameAndNotRecordedAddUpToTen()
    {
        var comparison = SessionTune.Compare(
            Tune(session => session with { FrontLowSpeedCompression = 2, FrontLowSpeedRebound = 5, RearSpringRate = "400" }),
            Tune(session => session with { FrontLowSpeedCompression = 2, FrontLowSpeedRebound = 6 }));

        Assert.Equal(10, comparison.Changes.Length + comparison.Same + comparison.NotRecorded);
    }

    private static SessionTune Tune(Func<SessionSnapshot, SessionSnapshot> edit) => SessionTune.From(edit(TestSnapshots.Session()));
}
