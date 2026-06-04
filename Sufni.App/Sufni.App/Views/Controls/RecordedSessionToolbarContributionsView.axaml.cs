using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.RecordedSessions;

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
            subscribedSlots.GraphToolbarPanels.CollectionChanged -= OnToolbarContributionsChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.GraphToolbarActions.CollectionChanged += OnToolbarContributionsChanged;
            subscribedSlots.GraphToolbarPanels.CollectionChanged += OnToolbarContributionsChanged;
        }
    }

    private void OnToolbarContributionsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        GraphToolbarActionsHost.Children.Clear();
        GraphToolbarPanelsHost.Children.Clear();
        if (ExtensionSlots is not { } slots)
        {
            return;
        }

        foreach (var contribution in slots.GraphToolbarActions.OrderBy(static contribution => contribution.Order))
        {
            GraphToolbarActionsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }

        foreach (var contribution in slots.GraphToolbarPanels.OrderBy(static contribution => contribution.Order))
        {
            GraphToolbarPanelsHost.Children.Add(CreateContributionControl(contribution.ViewModel));
        }
    }

    private static Control CreateContributionControl(object viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }
}
