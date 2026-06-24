using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ViewModels.Editors;

namespace Sufni.App.DesktopViews.Items;

public partial class SessionStatisticsDesktopView : UserControl
{
    private const string DefaultTabKey = "builtin:spring";
    private const int ExtensionFamilyOrder = 0;
    private const int BuiltInFamilyOrder = 1;

    private readonly List<StatisticsTabEntry> currentTabEntries = [];
    private readonly Dictionary<string, Control> extensionContentControls = new(StringComparer.Ordinal);
    private ISessionStatisticsWorkspace? workspace;
    private INotifyCollectionChanged? subscribedStatisticsTabs;
    private string selectedTabKey = DefaultTabKey;
    private bool suppressSelectionChanged;
    private bool isLoaded;

    public SessionStatisticsDesktopView()
    {
        InitializeComponent();

        // Set all pages visible at first, so that their plots are populated
        SpringRate.IsVisible = true;
        Strokes.IsVisible = true;
        Damping.IsVisible = true;
        Balance.IsVisible = true;
        Vibration.IsVisible = true;
        Analysis.IsVisible = true;

        DataContextChanged += (_, _) => SetWorkspace(DataContext as ISessionStatisticsWorkspace);
        TabControl.Loaded += (_, _) =>
        {
            isLoaded = true;
            RebuildTabs();
        };
        TabControl.SelectionChanged += (_, _) => OnTabSelectionChanged();

        SetWorkspace(DataContext as ISessionStatisticsWorkspace);
        RebuildTabs();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetWorkspace(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SetWorkspace(ISessionStatisticsWorkspace? value)
    {
        if (ReferenceEquals(workspace, value))
        {
            return;
        }

        if (subscribedStatisticsTabs is not null)
        {
            subscribedStatisticsTabs.CollectionChanged -= OnStatisticsTabsChanged;
            subscribedStatisticsTabs = null;
        }

        workspace = value;
        if (workspace is not null)
        {
            subscribedStatisticsTabs = workspace.ExtensionSlots.StatisticsTabs;
            subscribedStatisticsTabs.CollectionChanged += OnStatisticsTabsChanged;
        }

        RebuildTabs();
    }

    private void OnStatisticsTabsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        RebuildTabs();
    }

    private void OnTabSelectionChanged()
    {
        if (suppressSelectionChanged)
        {
            return;
        }

        if (TabControl.SelectedItem is TabItem { Tag: string key })
        {
            selectedTabKey = key;
        }

        ApplySelectedTabVisibility();
    }

    private void RebuildTabs()
    {
        var entries = CreateTabEntries().ToArray();
        RemoveStaleExtensionContentControls(entries);
        if (!entries.Any(entry => StringComparer.Ordinal.Equals(entry.Key, selectedTabKey)))
        {
            selectedTabKey = DefaultTabKey;
        }

        currentTabEntries.Clear();
        currentTabEntries.AddRange(entries);

        suppressSelectionChanged = true;
        try
        {
            TabControl.Items.Clear();
            foreach (var entry in entries)
            {
                TabControl.Items.Add(CreateTabItem(entry));
                if (entry.IsExtension)
                {
                    entry.Content.IsVisible = false;
                    if (!StatisticsContentHost.Items.Contains(entry.Content))
                    {
                        StatisticsContentHost.Items.Add(entry.Content);
                    }
                }
            }

            TabControl.SelectedIndex = Math.Max(
                0,
                Array.FindIndex(entries, entry => StringComparer.Ordinal.Equals(entry.Key, selectedTabKey)));
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        ApplySelectedTabVisibility();
    }

    private IEnumerable<StatisticsTabEntry> CreateTabEntries()
    {
        var builtInEntries = new[]
        {
            new StatisticsTabEntry(DefaultTabKey, "Spring rate", SpringRate, 0, 0, IsExtension: false, "", ""),
            new StatisticsTabEntry("builtin:strokes", "Strokes", Strokes, 1, 0, IsExtension: false, "", ""),
            new StatisticsTabEntry("builtin:damping", "Damping", Damping, 2, 0, IsExtension: false, "", ""),
            new StatisticsTabEntry("builtin:balance", "Balance", Balance, 3, 0, IsExtension: false, "", ""),
            new StatisticsTabEntry("builtin:vibration", "Vibration", Vibration, 4, 0, IsExtension: false, "", ""),
            new StatisticsTabEntry("builtin:analysis", "Analysis", Analysis, 5, 0, IsExtension: false, "", ""),
        };
        var extensionEntries = workspace?.ExtensionSlots.StatisticsTabs
            .Select(contribution => new StatisticsTabEntry(
                GetExtensionKey(contribution),
                contribution.DisplayName,
                GetExtensionContentControl(contribution),
                contribution.RequestedIndex,
                contribution.Order,
                IsExtension: true,
                contribution.ExtensionId,
                contribution.ContributionId))
            ?? [];

        return builtInEntries
            .Concat(extensionEntries)
            .OrderBy(entry => Math.Clamp(entry.RequestedIndex, 0, builtInEntries.Length))
            .ThenBy(entry => entry.IsExtension ? ExtensionFamilyOrder : BuiltInFamilyOrder)
            .ThenBy(entry => entry.IsExtension ? entry.Order : 0)
            .ThenBy(entry => entry.IsExtension ? entry.ExtensionId : "", StringComparer.Ordinal)
            .ThenBy(entry => entry.IsExtension ? entry.ContributionId : "", StringComparer.Ordinal);
    }

    private TabItem CreateTabItem(StatisticsTabEntry entry)
    {
        return new TabItem
        {
            Header = entry.Header,
            Tag = entry.Key,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = GetTabFontSize(),
        };
    }

    private double GetTabFontSize()
    {
        return Application.Current?.TryFindResource(
                "SufniTabFontSizeStatistics",
                ActualThemeVariant,
                out var resource) == true &&
            resource is double fontSize
                ? fontSize
                : 14;
    }

    private void ApplySelectedTabVisibility()
    {
        if (!isLoaded)
        {
            return;
        }

        var selectedEntry = currentTabEntries.FirstOrDefault(
            entry => StringComparer.Ordinal.Equals(entry.Key, selectedTabKey));
        if (selectedEntry is null)
        {
            selectedTabKey = DefaultTabKey;
            selectedEntry = currentTabEntries.FirstOrDefault(
                entry => StringComparer.Ordinal.Equals(entry.Key, selectedTabKey));
        }

        foreach (var entry in currentTabEntries)
        {
            entry.Content.IsVisible = ReferenceEquals(entry, selectedEntry);
        }
    }

    private Control GetExtensionContentControl(RecordedSessionStatisticsTabContribution contribution)
    {
        var key = GetExtensionKey(contribution);
        var nextViewModel = contribution.ViewModel;
        if (extensionContentControls.TryGetValue(key, out var existingControl))
        {
            if (nextViewModel is Control nextControl)
            {
                if (ReferenceEquals(existingControl, nextControl))
                {
                    return existingControl;
                }

                StatisticsContentHost.Items.Remove(existingControl);
                extensionContentControls[key] = nextControl;
                return nextControl;
            }

            if (existingControl is ContentControl contentControl)
            {
                contentControl.Content = nextViewModel;
                return contentControl;
            }

            StatisticsContentHost.Items.Remove(existingControl);
        }

        var control = nextViewModel as Control ?? new ContentControl { Content = nextViewModel };
        extensionContentControls[key] = control;
        return control;
    }

    private void RemoveStaleExtensionContentControls(IReadOnlyCollection<StatisticsTabEntry> entries)
    {
        var activeKeys = entries
            .Where(entry => entry.IsExtension)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var (key, control) in extensionContentControls.ToArray())
        {
            if (activeKeys.Contains(key))
            {
                continue;
            }

            StatisticsContentHost.Items.Remove(control);
            extensionContentControls.Remove(key);
        }
    }

    private static string GetExtensionKey(RecordedSessionStatisticsTabContribution contribution) =>
        $"extension:{contribution.ExtensionId}\u001f{contribution.ContributionId}";

    private sealed record StatisticsTabEntry(
        string Key,
        string Header,
        Control Content,
        int RequestedIndex,
        int Order,
        bool IsExtension,
        string ExtensionId,
        string ContributionId);
}
