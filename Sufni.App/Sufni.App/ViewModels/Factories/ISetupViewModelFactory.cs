using System;
using Sufni.App.Models;
using Sufni.App.ViewModels.Hosts;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public interface ISetupViewModelFactory
{
    SetupViewModel Create(Setup setup, Guid? boardId, bool fromDatabase, ISetupViewModelHost host);
}
