using System;
using Sufni.App.Models;
using Sufni.App.Services;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public class SetupViewModelFactory(
    IDatabaseService databaseService,
    IBikeSelectionSource bikeSelectionSource,
    INavigator navigator,
    IDialogService dialogService) : ISetupViewModelFactory
{
    public SetupViewModel Create(Setup setup, Guid? boardId, bool fromDatabase, ISetupViewModelHost host)
        => new(setup, boardId, fromDatabase, bikeSelectionSource, databaseService, navigator, dialogService, host);
}
