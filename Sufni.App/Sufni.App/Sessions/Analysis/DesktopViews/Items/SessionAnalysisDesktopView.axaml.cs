using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Analysis.DesktopViews.Items;

public partial class SessionAnalysisDesktopView : UserControl
{
    private const string DefaultTabKey = "builtin:spring";
    private const int ExtensionFamilyOrder = 0;
    private const int BuiltInFamilyOrder = 1;

    private readonly List<AnalysisTabEntry> currentTabEntries = [];
    private readonly Dictionary<string, Control> extensionContentControls = new(StringComparer.Ordinal);
    private ISessionAnalysisWorkspace? workspace;
    private INotifyCollectionChanged? subscribedAnalysisTabs;
    private string selectedTabKey = DefaultTabKey;
    private bool suppressSelectionChanged;
    private bool isLoaded;

    public SessionAnalysisDesktopView()
    {
        InitializeComponent();

        // Set all pages visible at first, so that their plots are populated
        SpringRate.IsVisible = true;
        Strokes.IsVisible = true;
        Damping.IsVisible = true;
        Balance.IsVisible = true;
        Vibration.IsVisible = true;
        Analysis.IsVisible = true;

        DataContextChanged += (_, _) => SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        TabControl.Loaded += (_, _) =>
        {
            isLoaded = true;
            RebuildTabs();
        };
        TabControl.SelectionChanged += (_, _) => OnTabSelectionChanged();

        SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        RebuildTabs();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SetWorkspace(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SetWorkspace(ISessionAnalysisWorkspace? value)
    {
        if (ReferenceEquals(workspace, value))
        {
            return;
        }

        if (subscribedAnalysisTabs is not null)
        {
            subscribedAnalysisTabs.CollectionChanged -= OnAnalysisTabsChanged;
            subscribedAnalysisTabs = null;
        }

        workspace = value;
        if (workspace is not null)
        {
            subscribedAnalysisTabs = workspace.ExtensionSlots.AnalysisTabs;
            subscribedAnalysisTabs.CollectionChanged += OnAnalysisTabsChanged;
        }

        RebuildTabs();
    }

    private void OnAnalysisTabsChanged(object? sender, NotifyCollectionChangedEventArgs args)
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
                    if (!AnalysisContentHost.Items.Contains(entry.Content))
                    {
                        AnalysisContentHost.Items.Add(entry.Content);
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

    private IEnumerable<AnalysisTabEntry> CreateTabEntries()
    {
        var builtInEntries = new[]
        {
            new AnalysisTabEntry(DefaultTabKey, "Spring rate", SpringRate, 0, 0, IsExtension: false, "", "",
                "How much of the available travel the fork and shock use and how often, for dialing in spring rate or air pressure."),
            new AnalysisTabEntry("builtin:strokes", "Strokes", Strokes, 1, 0, IsExtension: false, "", "",
                "Distribution of compression and rebound stroke length and speed, plus how often the suspension reaches deep travel."),
            new AnalysisTabEntry("builtin:damping", "Damping", Damping, 2, 0, IsExtension: false, "", "",
                "Suspension-speed distribution split into low- and high-speed compression and rebound zones, for tuning damping settings."),
            new AnalysisTabEntry("builtin:balance", "Balance", Balance, 3, 0, IsExtension: false, "", "",
                "Compares how the front and rear suspension move together to check the bike is balanced from end to end."),
            new AnalysisTabEntry("builtin:vibration", "Vibration", Vibration, 4, 0, IsExtension: false, "", "",
                "Vibration picked up by the IMU across the frequency range, and how smooth the ride is measured overall."),
            new AnalysisTabEntry("builtin:analysis", "Insights", Analysis, 5, 0, IsExtension: false, "", "",
                "Setup suggestions derived from the session data, interpreted through the selected riding context."),
        };
        var extensionEntries = workspace?.ExtensionSlots.AnalysisTabs
            .Select(contribution => new AnalysisTabEntry(
                GetExtensionKey(contribution),
                contribution.DisplayName,
                GetExtensionContentControl(contribution),
                contribution.RequestedIndex,
                contribution.Order,
                IsExtension: true,
                contribution.ExtensionId,
                contribution.ContributionId,
                Tooltip: ""))
            ?? [];

        return builtInEntries
            .Concat(extensionEntries)
            .OrderBy(entry => Math.Clamp(entry.RequestedIndex, 0, builtInEntries.Length))
            .ThenBy(entry => entry.IsExtension ? ExtensionFamilyOrder : BuiltInFamilyOrder)
            .ThenBy(entry => entry.IsExtension ? entry.Order : 0)
            .ThenBy(entry => entry.IsExtension ? entry.ExtensionId : "", StringComparer.Ordinal)
            .ThenBy(entry => entry.IsExtension ? entry.ContributionId : "", StringComparer.Ordinal);
    }

    private TabItem CreateTabItem(AnalysisTabEntry entry)
    {
        var tabItem = new TabItem
        {
            Header = entry.Header,
            Tag = entry.Key,
            HorizontalAlignment = HorizontalAlignment.Center,
            FontSize = GetTabFontSize(),
        };

        // Show a short description of the tab on hover (built-in tabs only).
        if (!string.IsNullOrEmpty(entry.Tooltip))
        {
            ToolTip.SetTip(tabItem, entry.Tooltip);
        }

        return tabItem;
    }

    private double GetTabFontSize()
    {
        return Application.Current?.TryFindResource(
                "SufniTabFontSizeAnalysis",
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

    private Control GetExtensionContentControl(RecordedSessionAnalysisTabContribution contribution)
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

                AnalysisContentHost.Items.Remove(existingControl);
                extensionContentControls[key] = nextControl;
                return nextControl;
            }

            if (existingControl is ContentControl contentControl)
            {
                contentControl.Content = nextViewModel;
                return contentControl;
            }

            AnalysisContentHost.Items.Remove(existingControl);
        }

        var control = nextViewModel as Control ?? new ContentControl { Content = nextViewModel };
        extensionContentControls[key] = control;
        return control;
    }

    private void RemoveStaleExtensionContentControls(IReadOnlyCollection<AnalysisTabEntry> entries)
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

            AnalysisContentHost.Items.Remove(control);
            extensionContentControls.Remove(key);
        }
    }

    private static string GetExtensionKey(RecordedSessionAnalysisTabContribution contribution) =>
        $"extension:{contribution.ExtensionId}\u001f{contribution.ContributionId}";

    private sealed record AnalysisTabEntry(
        string Key,
        string Header,
        Control Content,
        int RequestedIndex,
        int Order,
        bool IsExtension,
        string ExtensionId,
        string ContributionId,
        string Tooltip);
}
