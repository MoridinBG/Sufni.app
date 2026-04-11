using System;
using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DynamicData;
using Sufni.App.Stores;
using Sufni.App.ViewModels.Rows;

namespace Sufni.App.ViewModels.ItemLists;

public partial class LiveDaqListViewModel : ItemListViewModelBase
{
    private readonly ILiveDaqStore liveDaqStore;
    private readonly ReadOnlyObservableCollection<LiveDaqRowViewModel> liveDaqRows;
    private readonly BehaviorSubject<Func<LiveDaqSnapshot, bool>> filterSubject = new(_ => true);

    public ReadOnlyObservableCollection<LiveDaqRowViewModel> Items => liveDaqRows;

    [ObservableProperty] private string? requestedIdentityKey;

    public LiveDaqListViewModel()
    {
        liveDaqStore = null!;
        liveDaqRows = new ReadOnlyObservableCollection<LiveDaqRowViewModel>([]);
    }

    public LiveDaqListViewModel(ILiveDaqStore liveDaqStore)
    {
        this.liveDaqStore = liveDaqStore;

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
    }

    public void Deactivate()
    {
        DisposeScopedSubscriptions();
    }

    [RelayCommand]
    private void RowSelected(LiveDaqRowViewModel? row)
    {
        if (row is null) return;

        RequestedIdentityKey = row.IdentityKey;
    }
}