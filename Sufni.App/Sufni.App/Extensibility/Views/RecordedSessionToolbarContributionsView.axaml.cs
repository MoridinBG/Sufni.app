using System.Collections.Specialized;
using System.Linq;
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Svg.Skia;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
namespace Sufni.App.Extensibility.Views;

public partial class RecordedSessionToolbarContributionsView : UserControl
{
    private static readonly Uri SvgAssetBaseUri =
        new($"avares://{typeof(global::Sufni.App.App).Assembly.GetName().Name}/");

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
            subscribedSlots.SignalToolbarCommands.CollectionChanged -= OnToolbarContributionsChanged;
            subscribedSlots.SignalToolbarViews.CollectionChanged -= OnToolbarContributionsChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.SignalToolbarCommands.CollectionChanged += OnToolbarContributionsChanged;
            subscribedSlots.SignalToolbarViews.CollectionChanged += OnToolbarContributionsChanged;
        }
    }

    private void OnToolbarContributionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        LeadingSignalToolbarCommandBar.PrimaryCommands.Clear();
        TrailingSignalToolbarCommandBar.PrimaryCommands.Clear();
        LeadingSignalToolbarViewsHost.Children.Clear();
        TrailingSignalToolbarViewsHost.Children.Clear();
        if (ExtensionSlots is not { } slots)
        {
            return;
        }

        foreach (var contribution in OrderedToolbarCommandContributions(slots, RecordedSessionToolbarZone.Leading))
        {
            LeadingSignalToolbarCommandBar.PrimaryCommands.Add(CreateCommandBarButton(contribution));
        }

        foreach (var contribution in OrderedToolbarCommandContributions(slots, RecordedSessionToolbarZone.Trailing))
        {
            TrailingSignalToolbarCommandBar.PrimaryCommands.Add(CreateCommandBarButton(contribution));
        }

        foreach (var contribution in OrderedToolbarViewContributions(slots, RecordedSessionToolbarZone.Leading))
        {
            LeadingSignalToolbarViewsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }

        foreach (var contribution in OrderedToolbarViewContributions(slots, RecordedSessionToolbarZone.Trailing))
        {
            TrailingSignalToolbarViewsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private static IOrderedEnumerable<RecordedSessionToolbarCommandContribution> OrderedToolbarCommandContributions(
        RecordedSessionExtensionSlots slots,
        RecordedSessionToolbarZone zone)
    {
        return slots.SignalToolbarCommands
            .Where(contribution => contribution.Zone == zone)
            .OrderBy(static contribution => contribution.Order)
            .ThenBy(static contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(static contribution => contribution.ContributionId, StringComparer.Ordinal);
    }

    private static IOrderedEnumerable<RecordedSessionToolbarViewContribution> OrderedToolbarViewContributions(
        RecordedSessionExtensionSlots slots,
        RecordedSessionToolbarZone zone)
    {
        return slots.SignalToolbarViews
            .Where(contribution => contribution.Zone == zone)
            .OrderBy(static contribution => contribution.Order)
            .ThenBy(static contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(static contribution => contribution.ContributionId, StringComparer.Ordinal);
    }

    private static CommandBarButton CreateCommandBarButton(RecordedSessionToolbarCommandContribution contribution)
    {
        return new CommandBarButton
        {
            Label = contribution.Label,
            Icon = CreateIcon(contribution.Icon),
            Command = contribution.Command,
            CommandParameter = contribution.CommandParameter,
        };
    }

    private static Control CreateContributionControl(IExtensionViewModel viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }

    private static Image? CreateIcon(ToolbarIconDescriptor? descriptor)
    {
        return descriptor is null
            ? null
            : new Image
            {
                Width = descriptor.Width,
                Height = descriptor.Height,
                Source = new SvgImage { Source = SvgSource.Load(descriptor.AssetPath, SvgAssetBaseUri) },
            };
    }
}
