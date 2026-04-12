using System;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.App.Coordinators;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

// List-page view model for live preview. It projects the runtime live DAQ catalog,
// applies search filtering, and delegates browse lifecycle to the coordinator.
public partial class LiveDaqListViewModel : ItemListViewModelBase
{
    private readonly ILiveDaqStore liveDaqStore;
    private readonly ILiveDaqCoordinator liveDaqCoordinator;
    private readonly ReadOnlyObservableCollection<LiveDaqRowViewModel> liveDaqRows;
    private readonly BehaviorSubject<Func<LiveDaqSnapshot, bool>> filterSubject = new(_ => true);

    // Read-only row projection used by the desktop live DAQ list surface.
    public ReadOnlyObservableCollection<LiveDaqRowViewModel> Items => liveDaqRows;

    public LiveDaqListViewModel()
    {
        liveDaqStore = null!;
        liveDaqCoordinator = null!;
        liveDaqRows = new ReadOnlyObservableCollection<LiveDaqRowViewModel>([]);
    }

    public LiveDaqListViewModel(ILiveDaqStore liveDaqStore, ILiveDaqCoordinator liveDaqCoordinator)
    {
        this.liveDaqStore = liveDaqStore;
        this.liveDaqCoordinator = liveDaqCoordinator;

        liveDaqStore.Connect()
            .Filter(filterSubject)
            .TransformWithInlineUpdate(
                snapshot => new LiveDaqRowViewModel(snapshot),
                (row, snapshot) => row.Update(snapshot))
            .Bind(out liveDaqRows)
            .Subscribe();

        PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(SearchText)) RebuildFilter();
        };
    }

    protected override void RebuildFilter()
    {
        var current = SearchText;

        filterSubject.OnNext(snapshot =>
            string.IsNullOrWhiteSpace(current) ||
            snapshot.DisplayName.Contains(current, StringComparison.CurrentCultureIgnoreCase) ||
            (snapshot.BoardId?.Contains(current, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            (snapshot.Endpoint?.Contains(current, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            (snapshot.SetupName?.Contains(current, StringComparison.CurrentCultureIgnoreCase) ?? false) ||
            (snapshot.BikeName?.Contains(current, StringComparison.CurrentCultureIgnoreCase) ?? false));
    }

    public void Activate()
    {
        liveDaqCoordinator.Activate();
    }

    public void Deactivate()
    {
        liveDaqCoordinator.Deactivate();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task RowSelected(LiveDaqRowViewModel? row)
    {
        if (row is null) return;

        await liveDaqCoordinator.SelectAsync(row.IdentityKey);
    }
}