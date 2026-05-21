using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

public readonly record struct SessionDateGroupKey(DateOnly? Date)
{
    public static SessionDateGroupKey NoDate { get; } = new(null);

    public bool HasDate => Date.HasValue;

    public string ToDisplayText(CultureInfo culture) =>
        Date is { } date
            ? date.ToString("D", culture)
            : "No date";
}

public sealed partial class SessionDateGroupViewModel : ObservableObject
{
    [ObservableProperty] private bool isExpanded;

    public SessionDateGroupViewModel(SessionDateGroupKey key, bool isExpanded)
    {
        Key = key;
        HeaderText = key.ToDisplayText(CultureInfo.CurrentCulture);
        this.isExpanded = isExpanded;
        ToggleExpandedCommand = new RelayCommand(ToggleExpanded);
    }

    public SessionDateGroupKey Key { get; }

    public string HeaderText { get; }

    public ObservableCollection<SessionRowViewModel> Items { get; } = [];

    public IRelayCommand ToggleExpandedCommand { get; }

    public void SetRows(IEnumerable<SessionRowViewModel> rows)
    {
        var desiredRows = rows.ToList();

        for (var i = Items.Count - 1; i >= 0; i--)
        {
            if (!desiredRows.Contains(Items[i]))
            {
                Items.RemoveAt(i);
            }
        }

        for (var desiredIndex = 0; desiredIndex < desiredRows.Count; desiredIndex++)
        {
            var desiredRow = desiredRows[desiredIndex];
            if (desiredIndex < Items.Count && ReferenceEquals(Items[desiredIndex], desiredRow))
            {
                continue;
            }

            var existingIndex = Items.IndexOf(desiredRow);
            if (existingIndex >= 0)
            {
                Items.Move(existingIndex, desiredIndex);
            }
            else
            {
                Items.Insert(desiredIndex, desiredRow);
            }
        }
    }

    private void ToggleExpanded()
    {
        IsExpanded = !IsExpanded;
    }
}
