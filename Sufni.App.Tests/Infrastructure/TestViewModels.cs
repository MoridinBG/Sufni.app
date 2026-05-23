using Sufni.App.ViewModels;

namespace Sufni.App.Tests.Infrastructure;

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
        : base(new InlineUiThreadDispatcher())
    {
    }
}
