
using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Shell.Coordinators;
using Sufni.App.Shared.Base;
namespace Sufni.App.Tests.TestSupport.Doubles;

public sealed class TestViewModel : ViewModelBase
{
    public TestViewModel()
        : base(new InlineUiThreadDispatcher())
    {
    }
}

public sealed class TestTabPageViewModel : TabPageViewModelBase
{
    public TestTabPageViewModel()
        : base(Substitute.For<IShellCoordinator>(), Substitute.For<IDialogService>(), new InlineUiThreadDispatcher())
    {
    }
}
