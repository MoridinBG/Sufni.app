using Sufni.App.Terrain.ViewModels;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Terrain.ViewModels;

public class TerrainLabelClassPromptViewModelTests
{
    [Theory]
    [InlineData(null, null, false)]
    [InlineData(RoughnessClass.Rough, "The range overlaps a terrain label.", false)]
    [InlineData(RoughnessClass.Rough, null, true)]
    public void ConfirmNeedsAClassAndNoValidationMessage(RoughnessClass? selected, string? message, bool canConfirm)
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label terrain", "Label", selected, message);

        Assert.Equal(canConfirm, prompt.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public void ConfirmCompletesWithTheSelectedClass()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label terrain", "Label", null, null);
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.SelectedClass = RoughnessClass.TechnicalClimb;
        prompt.ConfirmCommand.Execute(null);

        Assert.True(completed);
        Assert.Equal(RoughnessClass.TechnicalClimb, prompt.ConfirmedClass);
    }

    [Fact]
    public void CancelCompletesWithoutAClass()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label terrain", "Label", RoughnessClass.Rough, null);
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.CancelCommand.Execute(null);

        Assert.True(completed);
        Assert.Null(prompt.ConfirmedClass);
    }

    [Fact]
    public void ClassesListEveryRoughnessClass()
    {
        var prompt = new TerrainLabelClassPromptViewModel("Label terrain", "Label", null, null);

        Assert.Equal(Enum.GetValues<RoughnessClass>(), prompt.Classes.Select(option => option.Class));
    }
}
