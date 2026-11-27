using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Terrain.ViewModels;

namespace Sufni.App.Tests.TestSupport.Extensions;

public static class DialogServiceTestExtensions
{
    /// <summary>Runs the work of each progress dialog that returns <typeparamref name="T"/>, without a dialog.</summary>
    public static void RunProgressWork<T>(this IDialogService dialogService) =>
        dialogService.ShowProgressAsync(Arg.Any<string>(), Arg.Any<Func<IProgress<DialogProgress>, Task<T>>>())
            .Returns(call => call.ArgAt<Func<IProgress<DialogProgress>, Task<T>>>(1)(Substitute.For<IProgress<DialogProgress>>()));

    /// <summary>Answers each terrain label class prompt with <paramref name="answer"/>.</summary>
    public static void AnswerClassPrompt(this IDialogService dialogService, Action<TerrainLabelClassPromptViewModel> answer) =>
        dialogService.ShowContentDialogAsync(Arg.Any<TerrainLabelClassPromptViewModel>(), Arg.Any<DialogOptions>())
            .Returns(call =>
            {
                answer(call.ArgAt<TerrainLabelClassPromptViewModel>(0));
                return PromptResult.Ok;
            });
}
