using System.Collections.Specialized;
using System.Collections.Generic;
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

    private readonly Dictionary<string, ExtensionViewModelLifetime.BorrowedControl> toolbarViewControls = new(StringComparer.Ordinal);
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
        ClearToolbarViewControls();
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
        if (ExtensionSlots is not { } slots)
        {
            ClearToolbarViewControls();
            return;
        }

        var leadingViews = OrderedToolbarViewContributions(slots, RecordedSessionToolbarZone.Leading).ToArray();
        var trailingViews = OrderedToolbarViewContributions(slots, RecordedSessionToolbarZone.Trailing).ToArray();
        RemoveStaleToolbarViewControls(leadingViews.Concat(trailingViews).ToArray());
        LeadingSignalToolbarViewsHost.Children.Clear();
        TrailingSignalToolbarViewsHost.Children.Clear();

        foreach (var contribution in OrderedToolbarCommandContributions(slots, RecordedSessionToolbarZone.Leading))
        {
            LeadingSignalToolbarCommandBar.PrimaryCommands.Add(CreateCommandBarButton(contribution));
        }

        foreach (var contribution in OrderedToolbarCommandContributions(slots, RecordedSessionToolbarZone.Trailing))
        {
            TrailingSignalToolbarCommandBar.PrimaryCommands.Add(CreateCommandBarButton(contribution));
        }

        foreach (var contribution in leadingViews)
        {
            AddContributionControl(LeadingSignalToolbarViewsHost, contribution);
        }

        foreach (var contribution in trailingViews)
        {
            AddContributionControl(TrailingSignalToolbarViewsHost, contribution);
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

    private void AddContributionControl(
        Panel host,
        RecordedSessionToolbarViewContribution contribution)
    {
        var borrowed = GetOrCreateToolbarViewControl(contribution);
        host.Children.Add(borrowed.Control);
    }

    private ExtensionViewModelLifetime.BorrowedControl GetOrCreateToolbarViewControl(
        RecordedSessionToolbarViewContribution contribution)
    {
        var key = GetContributionKey(contribution);
        if (toolbarViewControls.TryGetValue(key, out var borrowed) &&
            ReferenceEquals(borrowed.ViewModel, contribution.ViewModel))
        {
            return borrowed;
        }

        borrowed = ExtensionViewModelLifetime.CreateBorrowedControl(contribution.ViewModel);
        toolbarViewControls[key] = borrowed;
        return borrowed;
    }

    private void RemoveStaleToolbarViewControls(IReadOnlyCollection<RecordedSessionToolbarViewContribution> contributions)
    {
        var active = contributions.ToDictionary(GetContributionKey, contribution => contribution.ViewModel, StringComparer.Ordinal);
        foreach (var (key, borrowed) in toolbarViewControls.ToArray())
        {
            if (active.TryGetValue(key, out var viewModel) &&
                ReferenceEquals(borrowed.ViewModel, viewModel))
            {
                continue;
            }

            toolbarViewControls.Remove(key);
        }
    }

    private void ClearToolbarViewControls()
    {
        toolbarViewControls.Clear();
        LeadingSignalToolbarViewsHost.Children.Clear();
        TrailingSignalToolbarViewsHost.Children.Clear();
    }

    private static string GetContributionKey(RecordedSessionToolbarViewContribution contribution) =>
        $"{contribution.Zone}\u001f{contribution.ExtensionId}\u001f{contribution.ContributionId}";

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
