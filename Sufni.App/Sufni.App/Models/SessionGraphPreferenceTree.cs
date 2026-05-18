using System;
using System.Collections.Generic;
using System.Linq;

namespace Sufni.App.Models;

internal sealed record SessionGraphPreferenceRowState(
    string RowId,
    bool IsExpanded,
    IReadOnlyList<SessionGraphPreferenceRowState> Children);

// Pure preference-tree helper for recorded graph row ordering. It keeps invalid
// or stale row ids out of saved preferences and guards against cyclic moves.
internal static class SessionGraphPreferenceTree
{
    public static SessionGraphPreferences Normalize(
        SessionGraphPreferences? preferences,
        IEnumerable<string> availableRowIds,
        SessionGraphPreferences? defaults = null)
    {
        defaults ??= SessionGraphPreferences.Default;
        var availableIds = availableRowIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var available = availableIds.ToHashSet(StringComparer.Ordinal);
        var used = new HashSet<string>(StringComparer.Ordinal);
        var nodesById = new Dictionary<string, RowNode>(StringComparer.Ordinal);
        var rootRows = MaterializeRows((preferences ?? defaults).Rows, available, used, nodesById).ToList();

        AppendMissingDefaultRows(defaults.Rows, available, used, nodesById, rootRows, parent: null);

        foreach (var rowId in availableIds)
        {
            if (used.Add(rowId))
            {
                rootRows.Add(new RowNode(rowId, isExpanded: true, []));
            }
        }

        return new SessionGraphPreferences(rootRows.Select(ToPreferences).ToArray());
    }

    public static SessionGraphPreferences Capture(IEnumerable<SessionGraphPreferenceRowState> rows)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return new SessionGraphPreferences(CaptureRows(rows, used).ToArray());
    }

    public static SessionGraphPreferences MoveToRoot(
        SessionGraphPreferences preferences,
        string rowId,
        int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(rowId))
        {
            return preferences;
        }

        var roots = CloneRows(preferences.Rows).ToList();
        if (!TryRemove(roots, rowId, out var removed, out var source, out var sourceIndex))
        {
            return preferences;
        }

        if (ReferenceEquals(source, roots) && sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        roots.Insert(Math.Clamp(targetIndex, 0, roots.Count), removed);
        return new SessionGraphPreferences(roots.Select(ToPreferences).ToArray());
    }

    public static SessionGraphPreferences MoveInto(
        SessionGraphPreferences preferences,
        string rowId,
        string parentRowId,
        int targetIndex)
    {
        if (string.IsNullOrWhiteSpace(rowId) ||
            string.IsNullOrWhiteSpace(parentRowId) ||
            string.Equals(rowId, parentRowId, StringComparison.Ordinal))
        {
            return preferences;
        }

        var roots = CloneRows(preferences.Rows).ToList();
        var movingRow = Find(roots, rowId);
        if (movingRow is null || Contains(movingRow, parentRowId))
        {
            return preferences;
        }

        if (!TryRemove(roots, rowId, out var removed, out var source, out var sourceIndex))
        {
            return preferences;
        }

        var parent = Find(roots, parentRowId);
        if (parent is null)
        {
            return preferences;
        }

        if (ReferenceEquals(source, parent.Children) && sourceIndex < targetIndex)
        {
            targetIndex--;
        }

        parent.IsExpanded = true;
        parent.Children.Insert(Math.Clamp(targetIndex, 0, parent.Children.Count), removed);
        return new SessionGraphPreferences(roots.Select(ToPreferences).ToArray());
    }

    public static SessionGraphPreferences SetExpanded(
        SessionGraphPreferences preferences,
        string rowId,
        bool isExpanded)
    {
        if (string.IsNullOrWhiteSpace(rowId))
        {
            return preferences;
        }

        var roots = CloneRows(preferences.Rows).ToList();
        var row = Find(roots, rowId);
        if (row is null || row.IsExpanded == isExpanded)
        {
            return preferences;
        }

        row.IsExpanded = isExpanded;
        return new SessionGraphPreferences(roots.Select(ToPreferences).ToArray());
    }

    private static IEnumerable<RowNode> MaterializeRows(
        IEnumerable<SessionGraphRowPreferences> preferences,
        ISet<string> available,
        ISet<string> used,
        IDictionary<string, RowNode> nodesById)
    {
        foreach (var preference in preferences)
        {
            if (string.IsNullOrWhiteSpace(preference.RowId) ||
                !available.Contains(preference.RowId) ||
                !used.Add(preference.RowId))
            {
                continue;
            }

            var node = new RowNode(preference.RowId, preference.IsExpanded, []);
            nodesById.Add(preference.RowId, node);
            node.Children.AddRange(MaterializeRows(preference.Children, available, used, nodesById));
            yield return node;
        }
    }

    private static void AppendMissingDefaultRows(
        IEnumerable<SessionGraphRowPreferences> defaults,
        ISet<string> available,
        ISet<string> used,
        IDictionary<string, RowNode> nodesById,
        IList<RowNode> rootRows,
        RowNode? parent)
    {
        foreach (var preference in defaults)
        {
            if (string.IsNullOrWhiteSpace(preference.RowId) ||
                !available.Contains(preference.RowId))
            {
                continue;
            }

            if (!nodesById.TryGetValue(preference.RowId, out var node))
            {
                node = new RowNode(preference.RowId, preference.IsExpanded, []);
                nodesById.Add(preference.RowId, node);
                used.Add(preference.RowId);
                if (parent is null)
                {
                    rootRows.Add(node);
                }
                else
                {
                    parent.Children.Add(node);
                }
            }

            AppendMissingDefaultRows(preference.Children, available, used, nodesById, rootRows, node);
        }
    }

    private static IEnumerable<SessionGraphRowPreferences> CaptureRows(
        IEnumerable<SessionGraphPreferenceRowState> rows,
        ISet<string> used)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.RowId) || !used.Add(row.RowId))
            {
                continue;
            }

            yield return new SessionGraphRowPreferences(
                row.RowId,
                row.IsExpanded,
                CaptureRows(row.Children, used).ToArray());
        }
    }

    private static IEnumerable<RowNode> CloneRows(IEnumerable<SessionGraphRowPreferences> rows)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        return CloneRows(rows, used);
    }

    private static IEnumerable<RowNode> CloneRows(
        IEnumerable<SessionGraphRowPreferences> rows,
        ISet<string> used)
    {
        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.RowId) || !used.Add(row.RowId))
            {
                continue;
            }

            yield return new RowNode(row.RowId, row.IsExpanded, CloneRows(row.Children, used).ToList());
        }
    }

    private static bool TryRemove(
        List<RowNode> rows,
        string rowId,
        out RowNode removed,
        out List<RowNode> source,
        out int sourceIndex)
    {
        for (var i = 0; i < rows.Count; i++)
        {
            if (string.Equals(rows[i].RowId, rowId, StringComparison.Ordinal))
            {
                removed = rows[i];
                source = rows;
                sourceIndex = i;
                rows.RemoveAt(i);
                return true;
            }

            if (TryRemove(rows[i].Children, rowId, out removed, out source, out sourceIndex))
            {
                return true;
            }
        }

        removed = null!;
        source = null!;
        sourceIndex = -1;
        return false;
    }

    private static RowNode? Find(IEnumerable<RowNode> rows, string rowId)
    {
        foreach (var row in rows)
        {
            if (string.Equals(row.RowId, rowId, StringComparison.Ordinal))
            {
                return row;
            }

            var child = Find(row.Children, rowId);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static bool Contains(RowNode row, string rowId) =>
        row.Children.Any(child =>
            string.Equals(child.RowId, rowId, StringComparison.Ordinal) ||
            Contains(child, rowId));

    private static SessionGraphRowPreferences ToPreferences(RowNode node) =>
        new(node.RowId, node.IsExpanded, node.Children.Select(ToPreferences).ToArray());

    private sealed class RowNode(string rowId, bool isExpanded, List<RowNode> children)
    {
        public string RowId { get; } = rowId;
        public bool IsExpanded { get; set; } = isExpanded;
        public List<RowNode> Children { get; } = children;
    }
}
