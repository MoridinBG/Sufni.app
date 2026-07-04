using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;
using Sufni.App.Extensibility.Views;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;

using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
namespace Sufni.App.Sessions.Analysis.DesktopViews.Items;

public partial class SessionAnalysisDesktopView : UserControl
{
    private const string DefaultTabKey = "builtin:spring";
    private const string InsightsTabKey = "builtin:analysis";
    private const int ExtensionFamilyOrder = 0;
    private const int BuiltInFamilyOrder = 1;

    private readonly List<AnalysisTabEntry> currentTabEntries = [];
    private readonly Dictionary<string, ExtensionTabContent> extensionContentControls = new(StringComparer.Ordinal);
    private ISessionAnalysisWorkspace? workspace;
    private INotifyCollectionChanged? subscribedAnalysisTabs;
    private string selectedTabKey = DefaultTabKey;
    private bool suppressSelectionChanged;

    public SessionAnalysisDesktopView()
    {
        InitializeComponent();

        DataContextChanged += (_, _) => SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        TabControl.Loaded += (_, _) => RebuildTabs();
        TabControl.SelectionChanged += (_, _) => OnTabSelectionChanged();

        SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        RebuildTabs();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SetWorkspace(DataContext as ISessionAnalysisWorkspace);
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

        SyncHeaderDataContexts();
        RebuildTabs();
    }

    private void SyncHeaderDataContexts()
    {
        FrontTravelDistributionModeComboBox.DataContext = workspace;
        TravelDistributionModeComboBox.DataContext = workspace;
        CompressionBalanceHeaderPanel.DataContext = workspace;
        ReboundBalanceHeaderPanel.DataContext = workspace;
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
            new AnalysisTabEntry(InsightsTabKey, "Insights", Analysis, 5, 0, IsExtension: false, "", "",
                "Setup suggestions derived from the session data, interpreted through the selected riding context."),
        };
        var extensionEntries = workspace?.ExtensionSlots.AnalysisTabs
            .Select(contribution => new AnalysisTabEntry(
                GetExtensionKey(contribution),
                contribution.DisplayName,
                Content: null,
                contribution.RequestedIndex,
                contribution.Order,
                IsExtension: true,
                contribution.ExtensionId,
                contribution.ContributionId,
                Tooltip: "",
                ExtensionContribution: contribution))
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
            var isSelected = ReferenceEquals(entry, selectedEntry);
            if (entry.IsExtension)
            {
                if (isSelected && entry.ExtensionContribution is { } contribution)
                {
                    var control = GetExtensionContentControl(contribution);
                    control.IsVisible = true;
                    SetAnalysisDemandActive(control, true);
                }
                else if (extensionContentControls.TryGetValue(entry.Key, out var content))
                {
                    content.Control.IsVisible = false;
                    SetAnalysisDemandActive(content.Control, false);
                }

                continue;
            }

            if (entry.Content is not null)
            {
                entry.Content.IsVisible = isSelected;
                SetAnalysisDemandActive(entry.Content, isSelected);
                if (isSelected && StringComparer.Ordinal.Equals(entry.Key, InsightsTabKey))
                {
                    workspace?.RequestSessionInsights();
                }
            }
        }
    }

    private static void SetAnalysisDemandActive(Control content, bool isActive)
    {
        if (content is AnalysisHostBase host)
        {
            host.IsAnalysisDemandActive = isActive;
        }

        foreach (var descendantHost in content.GetVisualDescendants().OfType<AnalysisHostBase>())
        {
            descendantHost.IsAnalysisDemandActive = isActive;
        }
    }

    private Control GetExtensionContentControl(RecordedSessionAnalysisTabContribution contribution)
    {
        var key = GetExtensionKey(contribution);
        if (extensionContentControls.TryGetValue(key, out var existingContent))
        {
            return existingContent.Control;
        }

        var nextViewModel = contribution.CreateViewModel();
        var owner = ExtensionViewModelLifetime.CreateOwnedControl(nextViewModel);
        var control = owner.Control;
        control.IsVisible = false;
        extensionContentControls[key] = new ExtensionTabContent(contribution, owner);
        AnalysisContentHost.Items.Add(control);
        return control;
    }

    private void RemoveStaleExtensionContentControls(IReadOnlyCollection<AnalysisTabEntry> entries)
    {
        var activeContributions = entries
            .Where(entry => entry.IsExtension)
            .Where(entry => entry.ExtensionContribution is not null)
            .ToDictionary(entry => entry.Key, entry => entry.ExtensionContribution!, StringComparer.Ordinal);
        foreach (var (key, content) in extensionContentControls.ToArray())
        {
            if (activeContributions.TryGetValue(key, out var contribution) &&
                ReferenceEquals(content.Contribution, contribution))
            {
                continue;
            }

            AnalysisContentHost.Items.Remove(content.Control);
            extensionContentControls.Remove(key);
            content.Dispose();
        }
    }

    private static string GetExtensionKey(RecordedSessionAnalysisTabContribution contribution) =>
        $"extension:{contribution.ExtensionId}\u001f{contribution.ContributionId}";

    private sealed record AnalysisTabEntry(
        string Key,
        string Header,
        Control? Content,
        int RequestedIndex,
        int Order,
        bool IsExtension,
        string ExtensionId,
        string ContributionId,
        string Tooltip,
        RecordedSessionAnalysisTabContribution? ExtensionContribution = null);

    private sealed record ExtensionTabContent(
        RecordedSessionAnalysisTabContribution Contribution,
        ExtensionViewModelLifetime.OwnedControl Owner) : IDisposable
    {
        public Control Control => Owner.Control;

        public void Dispose()
        {
            Owner.Dispose();
        }
    }
}
