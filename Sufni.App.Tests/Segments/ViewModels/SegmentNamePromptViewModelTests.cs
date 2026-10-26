using Sufni.App.Segments.ViewModels;

namespace Sufni.App.Tests.Segments.ViewModels;

public class SegmentNamePromptViewModelTests
{
    [Fact]
    public void Confirm_WithInvalidName_ShowsValidationAndStaysOpen()
    {
        var prompt = new SegmentNamePromptViewModel("Save segment", "Save", "Climb", _ => "A saved segment with this name already exists.");
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.ConfirmCommand.Execute(null);

        Assert.True(prompt.HasValidationMessage);
        Assert.False(completed);
        Assert.Null(prompt.ConfirmedName);
    }

    [Fact]
    public void Confirm_WithValidName_CompletesWithTheTrimmedName()
    {
        var prompt = new SegmentNamePromptViewModel("Save segment", "Save", " Climb ", _ => null);
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.ConfirmCommand.Execute(null);

        Assert.True(completed);
        Assert.Equal("Climb", prompt.ConfirmedName);
    }

    [Fact]
    public void Cancel_CompletesWithoutAName()
    {
        var prompt = new SegmentNamePromptViewModel("Save segment", "Save", "Climb", _ => null);
        var completed = false;
        prompt.Completed += (_, _) => completed = true;

        prompt.CancelCommand.Execute(null);

        Assert.True(completed);
        Assert.Null(prompt.ConfirmedName);
    }
}
