namespace Sufni.App.ViewModels.SessionPages;

internal sealed class RecordedSessionExtensionPageViewModel(
    string displayName,
    object viewModel) : PageViewModelBase(displayName)
{
    public object ViewModel { get; } = viewModel;
}
