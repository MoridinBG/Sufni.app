using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

namespace Sufni.App.Extensibility.Views;

public partial class RecordedSessionAnalysisContributionsView : UserControl
{
    private readonly Dictionary<string, ExtensionViewModelLifetime.BorrowedControl> bannerControls = new(StringComparer.Ordinal);
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<RecordedSessionAnalysisContributionsView, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public RecordedSessionAnalysisContributionsView()
    {
        InitializeComponent();
        PropertyChanged += (_, args) =>
        {
            if (args.Property == ExtensionSlotsProperty)
            {
                SubscribeToSlots(ExtensionSlots);
                Rebuild();
            }
        };
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        SubscribeToSlots(ExtensionSlots);
        Rebuild();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        SubscribeToSlots(null);
        ClearBannerControls();
        base.OnDetachedFromVisualTree(e);
    }

    private void SubscribeToSlots(RecordedSessionExtensionSlots? slots)
    {
        if (ReferenceEquals(subscribedSlots, slots))
        {
            return;
        }

        if (subscribedSlots is not null)
        {
            subscribedSlots.AnalysisBanners.CollectionChanged -= OnAnalysisBannersChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.AnalysisBanners.CollectionChanged += OnAnalysisBannersChanged;
        }
    }

    private void OnAnalysisBannersChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        if (ExtensionSlots is not { } slots)
        {
            ClearBannerControls();
            return;
        }

        var contributions = slots.AnalysisBanners
            .OrderBy(static contribution => contribution.Order)
            .ToArray();
        RemoveStaleBannerControls(contributions);
        RecordedSessionAnalysisBannersHost.Children.Clear();
        foreach (var contribution in contributions)
        {
            var borrowed = GetOrCreateBannerControl(contribution);
            RecordedSessionAnalysisBannersHost.Children.Add(borrowed.Control);
        }
    }

    private ExtensionViewModelLifetime.BorrowedControl GetOrCreateBannerControl(
        RecordedSessionAnalysisBannerContribution contribution)
    {
        var key = GetContributionKey(contribution);
        if (bannerControls.TryGetValue(key, out var borrowed) &&
            ReferenceEquals(borrowed.ViewModel, contribution.ViewModel))
        {
            return borrowed;
        }

        borrowed = ExtensionViewModelLifetime.CreateBorrowedControl(contribution.ViewModel);
        bannerControls[key] = borrowed;
        return borrowed;
    }

    private void RemoveStaleBannerControls(IReadOnlyCollection<RecordedSessionAnalysisBannerContribution> contributions)
    {
        var active = contributions.ToDictionary(GetContributionKey, contribution => contribution.ViewModel, StringComparer.Ordinal);
        foreach (var (key, borrowed) in bannerControls.ToArray())
        {
            if (active.TryGetValue(key, out var viewModel) &&
                ReferenceEquals(borrowed.ViewModel, viewModel))
            {
                continue;
            }

            bannerControls.Remove(key);
        }
    }

    private void ClearBannerControls()
    {
        bannerControls.Clear();
        RecordedSessionAnalysisBannersHost.Children.Clear();
    }

    private static string GetContributionKey(RecordedSessionAnalysisBannerContribution contribution) =>
        $"{contribution.ExtensionId}\u001f{contribution.ContributionId}";
}
