using Avalonia.Headless.XUnit;
using NSubstitute;
using Sufni.App.Bikes.Models;
using Sufni.App.Tests.TestSupport.Fixtures;

namespace Sufni.App.Tests.Bikes.ViewModels.Editors;

[Collection("Ui")]
public class RearSuspensionWorkspaceTests : BikeEditorFixture
{
    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_RejectsChangesWithoutPointerCapability()
    {
        var editor = CreateEditor(TestSnapshots.Bike(), hasPointer: false);

        Assert.False(editor.RearSuspension.SetRearSuspensionModeCommand.CanExecute(RearSuspensionKind.Linkage));
        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.Linkage);

        Assert.Equal(RearSuspensionKind.None, editor.RearSuspension.RearSuspensionMode);
        Assert.False(editor.LeverageRatioEditor.CanEdit);
        await dialogService.DidNotReceiveWithAnyArgs().ShowConfirmationAsync(default!, default!);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetRearSuspensionModeCommand_KeepsDraftUntilConfirmation(bool accept)
    {
        var editor = CreateEditor(FullSuspensionSnapshot());
        var originalJoint = editor.Linkage.JointViewModels[0];
        var confirmation = new TaskCompletionSource<bool>();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(confirmation.Task);

        var task = editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.None);

        Assert.True(editor.RearSuspension.SetRearSuspensionModeCommand.IsRunning);
        Assert.False(editor.RearSuspension.SetRearSuspensionModeCommand.CanExecute(RearSuspensionKind.LeverageRatio));
        Assert.Equal(RearSuspensionKind.Linkage, editor.RearSuspension.RearSuspensionMode);
        Assert.Contains(originalJoint, editor.Linkage.JointViewModels);
        confirmation.SetResult(accept);
        await task.AwaitBoundedAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(accept ? RearSuspensionKind.None : RearSuspensionKind.Linkage, editor.RearSuspension.RearSuspensionMode);
        Assert.Equal(accept, editor.IsDirty);
        if (!accept) Assert.Contains(originalJoint, editor.Linkage.JointViewModels);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_ClearsWheelState_WhenSwitchingFromLinkageToLeverageRatio()
    {
        var snapshot = CalibratedFullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);

        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.LeverageRatio);

        Assert.Equal(RearSuspensionKind.LeverageRatio, editor.RearSuspension.RearSuspensionMode);
        Assert.Null(editor.Wheels.FrontWheelRimSize);
        Assert.Null(editor.Wheels.FrontWheelTireWidth);
        Assert.Null(editor.Wheels.FrontWheelDiameter);
        Assert.Null(editor.Wheels.RearWheelRimSize);
        Assert.Null(editor.Wheels.RearWheelTireWidth);
        Assert.Null(editor.Wheels.RearWheelDiameter);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_ClearsShockStroke_WhenSwitchingFromLinkageToHardtail()
    {
        var snapshot = FullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);
        Assert.NotNull(editor.Frame.ShockStroke);

        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.None);

        Assert.Equal(RearSuspensionKind.None, editor.RearSuspension.RearSuspensionMode);
        Assert.Null(editor.Frame.ShockStroke);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_PreservesShockStroke_WhenSwitchingBetweenLinkageAndLeverageRatio()
    {
        var snapshot = FullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);
        var originalShockStroke = editor.Frame.ShockStroke;
        Assert.NotNull(originalShockStroke);

        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.LeverageRatio);

        Assert.Equal(originalShockStroke, editor.Frame.ShockStroke);
    }

    [AvaloniaFact]
    public async Task SetRearSuspensionModeCommand_DoesNotReprompt_AfterOutgoingPayloadAlreadyDiscarded()
    {
        var snapshot = FullSuspensionSnapshot();
        dialogService.ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        var editor = CreateEditor(snapshot);

        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.None);
        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.Linkage);
        dialogService.ClearReceivedCalls();

        await editor.RearSuspension.SetRearSuspensionModeCommand.ExecuteAsync(RearSuspensionKind.LeverageRatio);

        Assert.Equal(RearSuspensionKind.LeverageRatio, editor.RearSuspension.RearSuspensionMode);
        await dialogService.DidNotReceiveWithAnyArgs()
            .ShowConfirmationAsync(Arg.Any<string>(), Arg.Any<string>());
    }
}
