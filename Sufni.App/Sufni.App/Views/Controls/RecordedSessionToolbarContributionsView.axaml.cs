using System.Collections.Specialized;
using System.Linq;
using System;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

namespace Sufni.App.Views.Controls;

public partial class RecordedSessionToolbarContributionsView : UserControl
{
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<RecordedSessionToolbarContributionsView, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public RecordedSessionToolbarContributionsView()
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
            subscribedSlots.GraphToolbarActions.CollectionChanged -= OnToolbarContributionsChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.GraphToolbarActions.CollectionChanged += OnToolbarContributionsChanged;
        }
    }

    private void OnToolbarContributionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        LeadingGraphToolbarActionsHost.Children.Clear();
        TrailingGraphToolbarActionsHost.Children.Clear();
        if (ExtensionSlots is not { } slots)
        {
            return;
        }

        foreach (var contribution in OrderedToolbarContributions(slots, RecordedSessionToolbarZone.Leading))
        {
            LeadingGraphToolbarActionsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }

        foreach (var contribution in OrderedToolbarContributions(slots, RecordedSessionToolbarZone.Trailing))
        {
            TrailingGraphToolbarActionsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private static IOrderedEnumerable<RecordedSessionToolbarContribution> OrderedToolbarContributions(
        RecordedSessionExtensionSlots slots,
        RecordedSessionToolbarZone zone)
    {
        return slots.GraphToolbarActions
            .Where(contribution => contribution.Zone == zone)
            .OrderBy(static contribution => contribution.Order)
            .ThenBy(static contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(static contribution => contribution.ContributionId, StringComparer.Ordinal);
    }

    private static Control CreateContributionControl(IExtensionViewModel viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }
}
