using System;
using System.Threading.Tasks;
using Sufni.App.ExtensionHost.Contracts.Services;

using Sufni.App.Shared.Base;
namespace Sufni.App.Shell.ViewModels;

public sealed class MainPrimaryPageViewModel : ViewModelBase
{
    private static readonly IUiThreadDispatcher DescriptorDispatcher = new InlineDescriptorDispatcher();

    public MainPrimaryPageViewModel(
        MainPrimaryPageRole role,
        string header,
        string iconPath,
        ViewModelBase content)
        : base(DescriptorDispatcher)
    {
        Role = role;
        Header = header;
        IconPath = iconPath;
        Content = content;
    }

    public MainPrimaryPageRole Role { get; }
    public string Header { get; }
    public string IconPath { get; }
    public ViewModelBase Content { get; }

    private sealed class InlineDescriptorDispatcher : IUiThreadDispatcher
    {
        public bool CheckAccess() => true;

        public void Post(Action action) => action();

        public Task InvokeAsync(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        public Task InvokeAsync(Func<Task> action) => action();

        public Task<T> InvokeAsync<T>(Func<T> action) => Task.FromResult(action());
    }
}
