using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System;

using Sufni.App.Extensibility.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.Sessions.Detail.ViewModels.Editors;
using Sufni.App.Sessions.Pages.ViewModels.SessionPages;
namespace Sufni.App.Extensibility.Views;

/// <summary>
/// Owns extension page composition for a recorded session: projects the
/// extension manager's page contributions into the editor's page collection
/// and serves extension-initiated page-selection requests.
/// </summary>
internal sealed class RecordedSessionExtensionPagesController : IDisposable
{
    private readonly RecordedSessionExtensionManager manager;
    private readonly RecordedSessionContext context;
    private readonly Dictionary<string, RecordedSessionExtensionPageViewModel> recordedSessionExtensionPages = [];
    private bool disposed;

    public RecordedSessionExtensionPagesController(
        RecordedSessionExtensionManager manager,
        RecordedSessionContext context)
    {
        this.manager = manager;
        this.context = context;
        manager.ExtensionSlots.Pages.CollectionChanged += OnRecordedSessionExtensionPagesChanged;
        manager.ExtensionSlots.AnalysisTabs.CollectionChanged += OnRecordedSessionExtensionPagesChanged;
    }

    public void RequestRecordedSessionExtensionPageSelection(string contributionId)
    {
        if (disposed)
        {
            return;
        }

        var contribution = manager.ExtensionSlots.Pages
            .Where(contribution => StringComparer.Ordinal.Equals(contribution.ContributionId, contributionId))
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (contribution is null ||
            !recordedSessionExtensionPages.TryGetValue(RecordedSessionPageKey(contribution), out var page))
        {
            return;
        }

        var pageIndex = context.Pages.IndexOf(page);
        if (pageIndex >= 0)
        {
            context.SelectedPageIndex = pageIndex;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        manager.ExtensionSlots.Pages.CollectionChanged -= OnRecordedSessionExtensionPagesChanged;
        manager.ExtensionSlots.AnalysisTabs.CollectionChanged -= OnRecordedSessionExtensionPagesChanged;

        foreach (var page in recordedSessionExtensionPages.Values)
        {
            context.Pages.Remove(page);
            page.Dispose();
        }

        recordedSessionExtensionPages.Clear();
    }

    private void OnRecordedSessionExtensionPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ApplyRecordedSessionExtensionPages();
    }

    private void ApplyRecordedSessionExtensionPages()
    {
        if (disposed)
        {
            return;
        }

        var pageEntries = manager.ExtensionSlots.Pages
            .OrderBy(contribution => contribution.RequestedIndex)
            .ThenBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ContributionId, StringComparer.Ordinal)
            .Select(contribution => new ExtensionPageEntry(
                RecordedSessionPageKey(contribution),
                contribution.DisplayName,
                () => contribution.ViewModel,
                contribution.RequestedIndex,
                FamilyOrder: 0,
                contribution.Order,
                contribution.ExtensionId,
                contribution.ContributionId,
                OwnsViewModel: false));
        var analysisTabEntries = manager.ExtensionSlots.AnalysisTabs
            .OrderBy(contribution => contribution.RequestedIndex)
            .ThenBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ContributionId, StringComparer.Ordinal)
            .Select(contribution => new ExtensionPageEntry(
                AnalysisTabPageKey(contribution),
                contribution.DisplayName,
                contribution.CreateViewModel,
                contribution.RequestedIndex + 1,
                FamilyOrder: 1,
                contribution.Order,
                contribution.ExtensionId,
                contribution.ContributionId,
                contribution.OwnsCreatedViewModel));
        var entries = pageEntries
            .Concat(analysisTabEntries)
            .OrderBy(entry => entry.RequestedIndex)
            .ThenBy(entry => entry.FamilyOrder)
            .ThenBy(entry => entry.Order)
            .ThenBy(entry => entry.ExtensionId, StringComparer.Ordinal)
            .ThenBy(entry => entry.ContributionId, StringComparer.Ordinal)
            .ToArray();
        var desiredKeys = entries
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in recordedSessionExtensionPages.ToArray())
        {
            context.Pages.Remove(entry.Value);
            if (!desiredKeys.Contains(entry.Key))
            {
                recordedSessionExtensionPages.Remove(entry.Key);
                entry.Value.Dispose();
            }
        }

        var insertedCount = 0;
        foreach (var entry in entries)
        {
            if (!recordedSessionExtensionPages.TryGetValue(entry.Key, out var page))
            {
                page = new RecordedSessionExtensionPageViewModel(
                    entry.DisplayName,
                    entry.CreateViewModel,
                    entry.OwnsViewModel);
                recordedSessionExtensionPages.Add(entry.Key, page);
            }

            var insertIndex = Math.Clamp(entry.RequestedIndex + insertedCount, 0, context.Pages.Count);
            context.Pages.Insert(insertIndex, page);
            insertedCount++;
        }
    }

    private static string RecordedSessionPageKey(RecordedSessionPageContribution contribution)
    {
        return $"page:{contribution.ExtensionId}\u001f{contribution.ContributionId}";
    }

    private static string AnalysisTabPageKey(RecordedSessionAnalysisTabContribution contribution)
    {
        return $"analysis:{contribution.ExtensionId}\u001f{contribution.ContributionId}";
    }

    private sealed record ExtensionPageEntry(
        string Key,
        string DisplayName,
        Func<IExtensionViewModel> CreateViewModel,
        int RequestedIndex,
        int FamilyOrder,
        int Order,
        string ExtensionId,
        string ContributionId,
        bool OwnsViewModel);
}
