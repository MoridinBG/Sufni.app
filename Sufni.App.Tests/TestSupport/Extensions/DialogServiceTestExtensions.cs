using NSubstitute;
using Sufni.App.Infrastructure;
using Sufni.App.Terrain.ViewModels;

namespace Sufni.App.Tests.TestSupport.Extensions;

public static class DialogServiceTestExtensions
{
    /// <summary>Answers each terrain label class prompt with <paramref name="answer"/>.</summary>
    public static void AnswerClassPrompt(this IDialogService dialogService, Action<TerrainLabelClassPromptViewModel> answer) =>
        dialogService.ShowContentDialogAsync(Arg.Any<TerrainLabelClassPromptViewModel>(), Arg.Any<DialogOptions>())
            .Returns(call =>
            {
                answer(call.ArgAt<TerrainLabelClassPromptViewModel>(0));
                return PromptResult.Ok;
            });
}
