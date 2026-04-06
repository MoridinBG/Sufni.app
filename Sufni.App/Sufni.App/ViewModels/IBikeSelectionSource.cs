using System.Collections.ObjectModel;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels;

public interface IBikeSelectionSource
{
    ReadOnlyObservableCollection<ItemViewModelBase> Bikes { get; }
}
