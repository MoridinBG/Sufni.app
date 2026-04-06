using Sufni.App.Models;
using Sufni.App.ViewModels.Items;

namespace Sufni.App.ViewModels.Factories;

public interface ISessionViewModelFactory
{
    SessionViewModel Create(Session session, bool fromDatabase);
}
