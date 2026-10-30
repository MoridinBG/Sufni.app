using Microsoft.Extensions.Time.Testing;
using Sufni.App.Shared.Base;
using Sufni.App.Shell.Coordinators;

namespace Sufni.App.Tests.TestSupport.Doubles;

internal sealed class TestPendingDeleteListViewModel : ItemListViewModelBase
{
    public TestPendingDeleteListViewModel(IPendingDeletes owner)
        : base(new InlineUiThreadDispatcher(), new FakeTimeProvider()) =>
        ObservePendingDeletes<PendingDeleteKey.Session>(owner);
}
