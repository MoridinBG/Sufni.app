using System;

namespace Sufni.App.ViewModels;

public interface IBikeUsageQuery
{
    bool IsBikeInUse(Guid bikeId);
}
