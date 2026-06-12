using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHosting.RecordedSessions;
using Sufni.App.ViewModels.SessionPages;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System;

namespace Sufni.App.ViewModels.Editors;

/// <summary>
/// Owns extension page composition for a recorded session: projects the
/// extension manager's page contributions into the editor's page collection
/// and serves extension-initiated page-selection requests.
/// </summary>
internal sealed class RecordedSessionExtensionPagesController
{
    private readonly RecordedSessionExtensionManager manager;
    private readonly ObservableCollection<PageViewModelBase> pages;
    private readonly Dictionary<string, PageViewModelBase> recordedSessionExtensionPages = [];

    public RecordedSessionExtensionPagesController(
        RecordedSessionExtensionManager manager,
        ObservableCollection<PageViewModelBase> pages)
    {
        this.manager = manager;
        this.pages = pages;
        manager.ExtensionSlots.Pages.CollectionChanged += OnRecordedSessionExtensionPagesChanged;
    }

    public void RequestRecordedSessionExtensionPageSelection(string contributionId)
    {
        var contribution = manager.ExtensionSlots.Pages
            .Where(contribution => StringComparer.Ordinal.Equals(contribution.ContributionId, contributionId))
            .OrderBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .FirstOrDefault();
        if (contribution is null ||
            !recordedSessionExtensionPages.TryGetValue(RecordedSessionExtensionPageKey(contribution), out var page))
        {
            return;
        }

        foreach (var currentPage in pages)
        {
            currentPage.Selected = false;
        }

        page.Selected = true;
    }

    private void OnRecordedSessionExtensionPagesChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        ApplyRecordedSessionExtensionPages();
    }

    private void ApplyRecordedSessionExtensionPages()
    {
        var contributions = manager.ExtensionSlots.Pages
            .OrderBy(contribution => contribution.RequestedIndex)
            .ThenBy(contribution => contribution.Order)
            .ThenBy(contribution => contribution.ExtensionId, StringComparer.Ordinal)
            .ThenBy(contribution => contribution.ContributionId, StringComparer.Ordinal)
            .ToArray();
        var desiredKeys = contributions
            .Select(RecordedSessionExtensionPageKey)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var entry in recordedSessionExtensionPages.ToArray())
        {
            pages.Remove(entry.Value);
            if (!desiredKeys.Contains(entry.Key))
            {
                recordedSessionExtensionPages.Remove(entry.Key);
            }
        }

        var insertedCount = 0;
        foreach (var contribution in contributions)
        {
            var key = RecordedSessionExtensionPageKey(contribution);
            if (!recordedSessionExtensionPages.TryGetValue(key, out var page))
            {
                page = new RecordedSessionExtensionPageViewModel(
                    contribution.DisplayName,
                    contribution.ViewModel);
                recordedSessionExtensionPages.Add(key, page);
            }

            var insertIndex = Math.Clamp(contribution.RequestedIndex + insertedCount, 0, pages.Count);
            pages.Insert(insertIndex, page);
            insertedCount++;
        }
    }

    private static string RecordedSessionExtensionPageKey(RecordedSessionPageContribution contribution)
    {
        return $"{contribution.ExtensionId}\u001f{contribution.ContributionId}";
    }
}
