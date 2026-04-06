using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

internal class SessionViewModelFactory(IDatabaseService databaseService, IHttpApiService httpApiService) : ISessionViewModelFactory
{
    public SessionViewModel Create(Session session, bool fromDatabase)
        => new(session, fromDatabase, databaseService, httpApiService);
}
