using System.Collections.Specialized;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.ExtensionHost.Contracts;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

using Sufni.App.ExtensionHost.Contracts.Capabilities;
namespace Sufni.App.Sessions.Media.Views.Controls;

public partial class RecordedSessionMediaPanesView : UserControl
{
    private RecordedSessionExtensionSlots? subscribedSlots;

    public static readonly StyledProperty<RecordedSessionExtensionSlots?> ExtensionSlotsProperty =
        AvaloniaProperty.Register<RecordedSessionMediaPanesView, RecordedSessionExtensionSlots?>(
            nameof(ExtensionSlots));

    public RecordedSessionExtensionSlots? ExtensionSlots
    {
        get => GetValue(ExtensionSlotsProperty);
        set => SetValue(ExtensionSlotsProperty, value);
    }

    public RecordedSessionMediaPanesView()
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
            subscribedSlots.MediaPanes.CollectionChanged -= OnMediaPanesChanged;
        }

        subscribedSlots = slots;
        if (subscribedSlots is not null)
        {
            subscribedSlots.MediaPanes.CollectionChanged += OnMediaPanesChanged;
        }
    }

    private void OnMediaPanesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        Rebuild();
    }

    private void Rebuild()
    {
        RecordedSessionMediaPanesHost.Children.Clear();
        RecordedSessionMediaPanesHost.RowDefinitions.Clear();
        if (ExtensionSlots is not { } slots)
        {
            return;
        }

        var contributions = slots.MediaPanes
            .OrderBy(static contribution => contribution.Order)
            .ToArray();
        for (var i = 0; i < contributions.Length; i++)
        {
            RecordedSessionMediaPanesHost.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            var control = CreateContributionControl(contributions[i].ViewModel);
            Grid.SetRow(control, i);
            RecordedSessionMediaPanesHost.Children.Add(control);
        }
    }

    private static Control CreateContributionControl(IExtensionViewModel viewModel)
    {
        return viewModel as Control ?? new ContentControl { Content = viewModel };
    }
}
