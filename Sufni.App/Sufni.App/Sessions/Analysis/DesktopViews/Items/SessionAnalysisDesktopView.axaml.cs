using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.VisualTree;

using Sufni.App.Sessions.Analysis.Views.Controls;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Shared.Views;
namespace Sufni.App.Sessions.Analysis.DesktopViews.Items;

public partial class SessionAnalysisDesktopView : UserControl
{
    private const string DefaultTabKey = "spring";
    private const string InsightsTabKey = "analysis";

    private readonly List<AnalysisTabEntry> currentTabEntries = [];
    private ISessionAnalysisWorkspace? workspace;
    private string selectedTabKey = DefaultTabKey;
    private bool suppressSelectionChanged;
    private readonly EffectiveVisibilityObserver effectiveVisibilityObserver;

    public SessionAnalysisDesktopView()
    {
        InitializeComponent();

        effectiveVisibilityObserver = new EffectiveVisibilityObserver(this, _ => ApplySelectedTabVisibility());
        DataContextChanged += (_, _) => SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        TabControl.Loaded += (_, _) => RebuildTabs();
        TabControl.SelectionChanged += (_, _) => OnTabSelectionChanged();

        SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        RebuildTabs();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        effectiveVisibilityObserver.Attach();
        SetWorkspace(DataContext as ISessionAnalysisWorkspace);
        ApplySelectedTabVisibility();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        effectiveVisibilityObserver.Detach();
        ApplySelectedTabVisibility();
        SetWorkspace(null);
        base.OnDetachedFromVisualTree(e);
    }

    private void SetWorkspace(ISessionAnalysisWorkspace? value)
    {
        if (ReferenceEquals(workspace, value))
        {
            return;
        }

        workspace = value;
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
        yield return new AnalysisTabEntry(DefaultTabKey, "Spring rate", SpringRate,
            "How much of the available travel the fork and shock use and how often, for dialing in spring rate or air pressure.");
        yield return new AnalysisTabEntry("strokes", "Strokes", Strokes,
            "Distribution of compression and rebound stroke length and speed, plus how often the suspension reaches deep travel.");
        yield return new AnalysisTabEntry("damping", "Damping", Damping,
            "Suspension-speed distribution split into low- and high-speed compression and rebound zones, for tuning damping settings.");
        if (workspace?.PositionVelocity is not null)
        {
            yield return new AnalysisTabEntry("position-velocity", "Position/velocity", PositionVelocity,
                "Wheel velocity against wheel position for every compression and rebound stroke, as traces or a density heatmap.");
        }

        yield return new AnalysisTabEntry("balance", "Balance", Balance,
            "Compares how the front and rear suspension move together to check the bike is balanced from end to end.");
        yield return new AnalysisTabEntry("vibration", "Vibration", Vibration,
            "Vibration picked up by the IMU across the frequency range, and how smooth the ride is measured overall.");
        yield return new AnalysisTabEntry(InsightsTabKey, "Insights", Analysis,
            "Setup suggestions derived from the session data, interpreted through the selected riding context.");
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

        ToolTip.SetTip(tabItem, entry.Tooltip);
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
        PositionVelocity.IsVisible = false;
        foreach (var entry in currentTabEntries)
        {
            var isSelected = StringComparer.Ordinal.Equals(entry.Key, selectedTabKey);
            var isDemandActive = isSelected && effectiveVisibilityObserver.IsEffectivelyVisible;
            entry.Content.IsVisible = isSelected;
            SetAnalysisDemandActive(entry.Content, isDemandActive);
            if (isDemandActive && StringComparer.Ordinal.Equals(entry.Key, InsightsTabKey))
            {
                workspace?.RequestSessionInsights();
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

    private sealed record AnalysisTabEntry(
        string Key,
        string Header,
        Control Content,
        string Tooltip);
}
