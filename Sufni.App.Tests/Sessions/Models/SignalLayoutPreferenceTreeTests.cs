
using Sufni.App.Infrastructure;
using Sufni.App.Sessions.Models;
namespace Sufni.App.Tests.Sessions.Models;

public class SignalLayoutPreferenceTreeTests
{
    [Fact]
    public void Normalize_WithNullPreferences_ReturnsAvailableDefaultTree()
    {
        var normalized = SignalLayoutPreferenceTree.Normalize(
            preferences: null,
            availableRowIds:
            [
                SignalRowIds.Travel,
                SignalRowIds.Velocity,
                SignalRowIds.Imu,
                SignalRowIds.PitchRoll,
                SignalRowIds.Speed,
                SignalRowIds.Elevation
            ]);

        Assert.Equal(SignalLayoutPreferences.Default, normalized);
    }

    [Fact]
    public void Normalize_RemovesDuplicatesSkipsUnknownRowsAndAppendsMissingDefaults()
    {
        var preferences = new SignalLayoutPreferences(
        [
            Row(
                SignalRowIds.Speed,
                isExpanded: false,
                Row("unknown"),
                Row(SignalRowIds.Elevation, isExpanded: false)),
            Row(SignalRowIds.Travel, isExpanded: false),
            Row(SignalRowIds.Travel),
            Row("missing")
        ]);

        var normalized = SignalLayoutPreferenceTree.Normalize(
            preferences,
            availableRowIds:
            [
                SignalRowIds.Travel,
                SignalRowIds.Velocity,
                SignalRowIds.Imu,
                SignalRowIds.PitchRoll,
                SignalRowIds.Speed,
                SignalRowIds.Elevation
            ]);

        Assert.Equal(
        [
            Row(
                SignalRowIds.Speed,
                isExpanded: false,
                Row(SignalRowIds.Elevation, isExpanded: false)),
            Row(
                SignalRowIds.Travel,
                isExpanded: false,
                Row(SignalRowIds.Velocity)),
            Row(
                SignalRowIds.Imu,
                children:
                [
                    Row(SignalRowIds.PitchRoll)
                ])
        ], normalized.Rows);
    }

    [Fact]
    public void Capture_RemovesBlankAndDuplicateRows()
    {
        var captured = SignalLayoutPreferenceTree.Capture(
        [
            State(
                SignalRowIds.Travel,
                isExpanded: false,
                State(SignalRowIds.Velocity)),
            State(""),
            State(SignalRowIds.Travel),
        ]);

        Assert.Equal(
        [
            Row(
                SignalRowIds.Travel,
                isExpanded: false,
                Row(SignalRowIds.Velocity))
        ], captured.Rows);
    }

    [Fact]
    public void MoveToRoot_MovesChildToRequestedRootIndex()
    {
        var moved = SignalLayoutPreferenceTree.MoveToRoot(
            SignalLayoutPreferences.Default,
            SignalRowIds.Velocity,
            targetIndex: 0);

        Assert.Equal(SignalRowIds.Velocity, moved.Rows[0].RowId);
        Assert.Equal(SignalRowIds.Travel, moved.Rows[1].RowId);
        Assert.Empty(moved.Rows[1].Children);
    }

    [Fact]
    public void MoveInto_MovesRootIntoParentAtRequestedIndexAndExpandsParent()
    {
        var collapsedTravel = new SignalLayoutPreferences(
        [
            Row(
                SignalRowIds.Travel,
                isExpanded: false,
                Row(SignalRowIds.Velocity)),
            Row(SignalRowIds.Imu),
            Row(
                SignalRowIds.Speed,
                children:
                [
                    Row(SignalRowIds.Elevation)
                ])
        ]);

        var moved = SignalLayoutPreferenceTree.MoveInto(
            collapsedTravel,
            SignalRowIds.Imu,
            SignalRowIds.Travel,
            targetIndex: 1);

        Assert.Equal(SignalRowIds.Travel, moved.Rows[0].RowId);
        Assert.True(moved.Rows[0].IsExpanded);
        Assert.Equal(
        [
            SignalRowIds.Velocity,
            SignalRowIds.Imu
        ], moved.Rows[0].Children.Select(row => row.RowId));
        Assert.DoesNotContain(moved.Rows, row => row.RowId == SignalRowIds.Imu);
    }

    [Fact]
    public void MoveInto_RejectsMovingParentIntoDescendant()
    {
        var preferences = SignalLayoutPreferences.Default;

        var moved = SignalLayoutPreferenceTree.MoveInto(
            preferences,
            SignalRowIds.Travel,
            SignalRowIds.Velocity,
            targetIndex: 0);

        Assert.Equal(preferences, moved);
    }

    [Fact]
    public void SetExpanded_UpdatesOnlyTargetRow()
    {
        var updated = SignalLayoutPreferenceTree.SetExpanded(
            SignalLayoutPreferences.Default,
            SignalRowIds.Speed,
            isExpanded: false);

        Assert.False(updated.Rows.Single(row => row.RowId == SignalRowIds.Speed).IsExpanded);
        Assert.True(updated.Rows.Single(row => row.RowId == SignalRowIds.Travel).IsExpanded);
    }

    private static SignalLayoutRowPreferences Row(
        string rowId,
        bool isExpanded = true,
        params SignalLayoutRowPreferences[] children) =>
        new(rowId, isExpanded, children);

    private static SignalLayoutPreferenceRowState State(
        string rowId,
        bool isExpanded = true,
        params SignalLayoutPreferenceRowState[] children) =>
        new(rowId, isExpanded, HeightRatio: null, Children: children);
}
