using Sufni.App.Models;

namespace Sufni.App.Tests.Models;

public class SessionGraphPreferenceTreeTests
{
    [Fact]
    public void Normalize_WithNullPreferences_ReturnsAvailableDefaultTree()
    {
        var normalized = SessionGraphPreferenceTree.Normalize(
            preferences: null,
            availableRowIds:
            [
                TelemetryGraphRowIds.Travel,
                TelemetryGraphRowIds.Velocity,
                TelemetryGraphRowIds.Imu,
                TelemetryGraphRowIds.PitchRoll,
                TelemetryGraphRowIds.Speed,
                TelemetryGraphRowIds.Elevation
            ]);

        Assert.Equal(SessionGraphPreferences.Default, normalized);
    }

    [Fact]
    public void Normalize_RemovesDuplicatesSkipsUnknownRowsAndAppendsMissingDefaults()
    {
        var preferences = new SessionGraphPreferences(
        [
            Row(
                TelemetryGraphRowIds.Speed,
                isExpanded: false,
                Row("unknown"),
                Row(TelemetryGraphRowIds.Elevation, isExpanded: false)),
            Row(TelemetryGraphRowIds.Travel, isExpanded: false),
            Row(TelemetryGraphRowIds.Travel),
            Row("missing")
        ]);

        var normalized = SessionGraphPreferenceTree.Normalize(
            preferences,
            availableRowIds:
            [
                TelemetryGraphRowIds.Travel,
                TelemetryGraphRowIds.Velocity,
                TelemetryGraphRowIds.Imu,
                TelemetryGraphRowIds.PitchRoll,
                TelemetryGraphRowIds.Speed,
                TelemetryGraphRowIds.Elevation
            ]);

        Assert.Equal(
        [
            Row(
                TelemetryGraphRowIds.Speed,
                isExpanded: false,
                Row(TelemetryGraphRowIds.Elevation, isExpanded: false)),
            Row(
                TelemetryGraphRowIds.Travel,
                isExpanded: false,
                Row(TelemetryGraphRowIds.Velocity)),
            Row(
                TelemetryGraphRowIds.Imu,
                children:
                [
                    Row(TelemetryGraphRowIds.PitchRoll)
                ])
        ], normalized.Rows);
    }

    [Fact]
    public void Capture_RemovesBlankAndDuplicateRows()
    {
        var captured = SessionGraphPreferenceTree.Capture(
        [
            State(
                TelemetryGraphRowIds.Travel,
                isExpanded: false,
                State(TelemetryGraphRowIds.Velocity)),
            State(""),
            State(TelemetryGraphRowIds.Travel),
        ]);

        Assert.Equal(
        [
            Row(
                TelemetryGraphRowIds.Travel,
                isExpanded: false,
                Row(TelemetryGraphRowIds.Velocity))
        ], captured.Rows);
    }

    [Fact]
    public void MoveToRoot_MovesChildToRequestedRootIndex()
    {
        var moved = SessionGraphPreferenceTree.MoveToRoot(
            SessionGraphPreferences.Default,
            TelemetryGraphRowIds.Velocity,
            targetIndex: 0);

        Assert.Equal(TelemetryGraphRowIds.Velocity, moved.Rows[0].RowId);
        Assert.Equal(TelemetryGraphRowIds.Travel, moved.Rows[1].RowId);
        Assert.Empty(moved.Rows[1].Children);
    }

    [Fact]
    public void MoveInto_MovesRootIntoParentAtRequestedIndexAndExpandsParent()
    {
        var collapsedTravel = new SessionGraphPreferences(
        [
            Row(
                TelemetryGraphRowIds.Travel,
                isExpanded: false,
                Row(TelemetryGraphRowIds.Velocity)),
            Row(TelemetryGraphRowIds.Imu),
            Row(
                TelemetryGraphRowIds.Speed,
                children:
                [
                    Row(TelemetryGraphRowIds.Elevation)
                ])
        ]);

        var moved = SessionGraphPreferenceTree.MoveInto(
            collapsedTravel,
            TelemetryGraphRowIds.Imu,
            TelemetryGraphRowIds.Travel,
            targetIndex: 1);

        Assert.Equal(TelemetryGraphRowIds.Travel, moved.Rows[0].RowId);
        Assert.True(moved.Rows[0].IsExpanded);
        Assert.Equal(
        [
            TelemetryGraphRowIds.Velocity,
            TelemetryGraphRowIds.Imu
        ], moved.Rows[0].Children.Select(row => row.RowId));
        Assert.DoesNotContain(moved.Rows, row => row.RowId == TelemetryGraphRowIds.Imu);
    }

    [Fact]
    public void MoveInto_RejectsMovingParentIntoDescendant()
    {
        var preferences = SessionGraphPreferences.Default;

        var moved = SessionGraphPreferenceTree.MoveInto(
            preferences,
            TelemetryGraphRowIds.Travel,
            TelemetryGraphRowIds.Velocity,
            targetIndex: 0);

        Assert.Equal(preferences, moved);
    }

    [Fact]
    public void SetExpanded_UpdatesOnlyTargetRow()
    {
        var updated = SessionGraphPreferenceTree.SetExpanded(
            SessionGraphPreferences.Default,
            TelemetryGraphRowIds.Speed,
            isExpanded: false);

        Assert.False(updated.Rows.Single(row => row.RowId == TelemetryGraphRowIds.Speed).IsExpanded);
        Assert.True(updated.Rows.Single(row => row.RowId == TelemetryGraphRowIds.Travel).IsExpanded);
    }

    private static SessionGraphRowPreferences Row(
        string rowId,
        bool isExpanded = true,
        params SessionGraphRowPreferences[] children) =>
        new(rowId, isExpanded, children);

    private static SessionGraphPreferenceRowState State(
        string rowId,
        bool isExpanded = true,
        params SessionGraphPreferenceRowState[] children) =>
        new(rowId, isExpanded, children);
}
