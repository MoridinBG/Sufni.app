using Sufni.App.Presentation;

namespace Sufni.App.ViewModels.Editors;

public sealed record SessionGraphLayout(
    int TravelRow,
    int VelocityRow,
    int ImuRow,
    int PitchRollRow,
    int SpeedRow,
    int ElevationRow,
    SurfacePresentationState FirstState,
    SurfacePresentationState SecondState,
    SurfacePresentationState ThirdState,
    SurfacePresentationState FourthState,
    SurfacePresentationState FifthState,
    SurfacePresentationState SixthState,
    bool FirstSplitterVisible,
    bool SecondSplitterVisible,
    bool ThirdSplitterVisible,
    bool FourthSplitterVisible,
    bool FifthSplitterVisible)
{
    public static SessionGraphLayout Empty { get; } = Create(
        SurfacePresentationState.Hidden,
        SurfacePresentationState.Hidden,
        SurfacePresentationState.Hidden,
        SurfacePresentationState.Hidden,
        SurfacePresentationState.Hidden,
        SurfacePresentationState.Hidden);

    public static SessionGraphLayout Create(
        SurfacePresentationState travelState,
        SurfacePresentationState velocityState,
        SurfacePresentationState imuState,
        SurfacePresentationState pitchRollState,
        SurfacePresentationState speedState,
        SurfacePresentationState elevationState)
    {
        var firstState = SurfacePresentationState.Hidden;
        var secondState = SurfacePresentationState.Hidden;
        var thirdState = SurfacePresentationState.Hidden;
        var fourthState = SurfacePresentationState.Hidden;
        var fifthState = SurfacePresentationState.Hidden;
        var sixthState = SurfacePresentationState.Hidden;
        var travelRow = 0;
        var velocityRow = 0;
        var imuRow = 0;
        var pitchRollRow = 0;
        var speedRow = 0;
        var elevationRow = 0;
        var slot = 0;

        Add(travelState, ref slot, ref travelRow, ref firstState, ref secondState, ref thirdState, ref fourthState, ref fifthState, ref sixthState);
        Add(velocityState, ref slot, ref velocityRow, ref firstState, ref secondState, ref thirdState, ref fourthState, ref fifthState, ref sixthState);
        Add(imuState, ref slot, ref imuRow, ref firstState, ref secondState, ref thirdState, ref fourthState, ref fifthState, ref sixthState);
        Add(pitchRollState, ref slot, ref pitchRollRow, ref firstState, ref secondState, ref thirdState, ref fourthState, ref fifthState, ref sixthState);
        Add(speedState, ref slot, ref speedRow, ref firstState, ref secondState, ref thirdState, ref fourthState, ref fifthState, ref sixthState);
        Add(elevationState, ref slot, ref elevationRow, ref firstState, ref secondState, ref thirdState, ref fourthState, ref fifthState, ref sixthState);

        return new SessionGraphLayout(
            travelRow,
            velocityRow,
            imuRow,
            pitchRollRow,
            speedRow,
            elevationRow,
            firstState,
            secondState,
            thirdState,
            fourthState,
            fifthState,
            sixthState,
            firstState.ReservesLayout && secondState.ReservesLayout,
            secondState.ReservesLayout && thirdState.ReservesLayout,
            thirdState.ReservesLayout && fourthState.ReservesLayout,
            fourthState.ReservesLayout && fifthState.ReservesLayout,
            fifthState.ReservesLayout && sixthState.ReservesLayout);
    }

    private static void Add(
        SurfacePresentationState state,
        ref int slot,
        ref int row,
        ref SurfacePresentationState firstState,
        ref SurfacePresentationState secondState,
        ref SurfacePresentationState thirdState,
        ref SurfacePresentationState fourthState,
        ref SurfacePresentationState fifthState,
        ref SurfacePresentationState sixthState)
    {
        if (!state.ReservesLayout)
        {
            return;
        }

        row = slot * 2;
        switch (slot)
        {
            case 0:
                firstState = state;
                break;
            case 1:
                secondState = state;
                break;
            case 2:
                thirdState = state;
                break;
            case 3:
                fourthState = state;
                break;
            case 4:
                fifthState = state;
                break;
            case 5:
                sixthState = state;
                break;
        }

        slot++;
    }
}
