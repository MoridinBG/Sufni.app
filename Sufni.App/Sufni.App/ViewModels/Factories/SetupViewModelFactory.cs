using System;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public class SetupViewModelFactory(IDatabaseService databaseService, IBikeSelectionSource bikeSelectionSource) : ISetupViewModelFactory
{
    public SetupViewModel Create(Setup setup, Guid? boardId, bool fromDatabase)
        => new(setup, boardId, fromDatabase, bikeSelectionSource, databaseService);
}
