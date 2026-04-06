using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

internal class SessionViewModelFactory(
    IDatabaseService databaseService,
    IHttpApiService httpApiService,
    INavigator navigator,
    IDialogService dialogService) : ISessionViewModelFactory
{
    public SessionViewModel Create(Session session, bool fromDatabase, IItemDeletionHost host)
        => new(session, fromDatabase, databaseService, httpApiService, navigator, dialogService, host);
}
