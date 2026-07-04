using System;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Sufni.App.Extensibility.Views;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;

namespace Sufni.App.Sessions.Media.Views.Controls;

public partial class RecordedSessionMediaPanesView : UserControl
{
    private readonly Dictionary<string, ExtensionViewModelLifetime.BorrowedControl> mediaPaneControls = new(StringComparer.Ordinal);
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
        ClearMediaPaneControls();
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
        RecordedSessionMediaPanesHost.RowDefinitions.Clear();
        if (ExtensionSlots is not { } slots)
        {
            ClearMediaPaneControls();
            return;
        }

        var contributions = slots.MediaPanes
            .OrderBy(static contribution => contribution.Order)
            .ToArray();
        RemoveStaleMediaPaneControls(contributions);
        RecordedSessionMediaPanesHost.Children.Clear();
        for (var i = 0; i < contributions.Length; i++)
        {
            RecordedSessionMediaPanesHost.RowDefinitions.Add(new RowDefinition(new GridLength(1, GridUnitType.Star)));

            var borrowed = GetOrCreateMediaPaneControl(contributions[i]);
            var control = borrowed.Control;
            Grid.SetRow(control, i);
            RecordedSessionMediaPanesHost.Children.Add(control);
        }
    }

    private ExtensionViewModelLifetime.BorrowedControl GetOrCreateMediaPaneControl(
        RecordedSessionMediaPaneContribution contribution)
    {
        var key = GetContributionKey(contribution);
        if (mediaPaneControls.TryGetValue(key, out var borrowed) &&
            ReferenceEquals(borrowed.ViewModel, contribution.ViewModel))
        {
            return borrowed;
        }

        borrowed = ExtensionViewModelLifetime.CreateBorrowedControl(contribution.ViewModel);
        mediaPaneControls[key] = borrowed;
        return borrowed;
    }

    private void RemoveStaleMediaPaneControls(IReadOnlyCollection<RecordedSessionMediaPaneContribution> contributions)
    {
        var active = contributions.ToDictionary(GetContributionKey, contribution => contribution.ViewModel, StringComparer.Ordinal);
        foreach (var (key, borrowed) in mediaPaneControls.ToArray())
        {
            if (active.TryGetValue(key, out var viewModel) &&
                ReferenceEquals(borrowed.ViewModel, viewModel))
            {
                continue;
            }

            mediaPaneControls.Remove(key);
        }
    }

    private void ClearMediaPaneControls()
    {
        mediaPaneControls.Clear();
        RecordedSessionMediaPanesHost.Children.Clear();
    }

    private static string GetContributionKey(RecordedSessionMediaPaneContribution contribution) =>
        $"{contribution.ExtensionId}\u001f{contribution.ContributionId}";
}
