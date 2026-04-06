using System;
using System.Linq;
using System.Threading.Tasks;
using Sufni.App.Models;
using Sufni.App.Services;

namespace Sufni.App.Queries;

public sealed class BikeDependencyQuery(IDatabaseService databaseService) : IBikeDependencyQuery
{
    public async Task<bool> IsBikeInUseAsync(Guid bikeId)
    {
        var setups = await databaseService.GetAllAsync<Setup>();
        return setups.Any(s => s?.BikeId == bikeId);
    }
}
