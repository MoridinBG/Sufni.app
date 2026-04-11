using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.Models.SensorConfigurations;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.ViewModels.SensorConfigurations;
using Sufni.App.Views.Controls;
using Sufni.App.Views.Editors;
using Sufni.App.Views.SensorConfigurations;

namespace Sufni.App.Tests.Views.Editors;

public class SetupEditorViewTests
{
    [AvaloniaFact]
    public async Task SetupEditorView_BoardIdTextBox_DisplaysBoundValue()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var boardId = Guid.NewGuid();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike, boardId));

        var boardIdTextBox = mounted.View.FindControl<TextBox>("BoardIdTextBox");
        Assert.NotNull(boardIdTextBox);
        Assert.Equal(boardId.ToString(), boardIdTextBox!.Text);
    }

    [AvaloniaFact]
    public async Task SetupEditorView_BikeComboBox_PopulatedAndSelected()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike("Enduro bike");
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        var bikeComboBox = mounted.View.FindControl<ComboBox>("BikeComboBox");
        Assert.NotNull(bikeComboBox);
        Assert.Single(mounted.Editor.Bikes);
        Assert.Equal(bike.Id, mounted.Editor.SelectedBike?.Id);
        Assert.Equal(bike.Id, Assert.IsType<BikeSnapshot>(bikeComboBox!.SelectedItem).Id);
    }

    [AvaloniaFact]
    public async Task SetupEditorView_UsesEditableTitle()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        Assert.NotNull(mounted.View.FindFirstVisual<EditableTitle>());
    }

    [AvaloniaFact]
    public async Task SetupEditorView_UsesCommonButtonLine()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        Assert.NotNull(mounted.View.FindFirstVisual<CommonButtonLine>());
    }

    [AvaloniaFact]
    public async Task SetupEditorView_ForkSensorConfigContent_ShowsCorrectView()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        var forkContent = mounted.View.FindControl<ContentControl>("ForkSensorConfigContent");
        Assert.NotNull(forkContent);
        Assert.NotNull(forkContent!.FindFirstVisual<LinearForkSensorConfigurationView>());
    }

    [AvaloniaFact]
    public async Task SetupEditorView_ShockSensorConfigContent_ShowsCorrectView()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        var shockContent = mounted.View.FindControl<ContentControl>("ShockSensorConfigContent");
        Assert.NotNull(shockContent);
        Assert.NotNull(shockContent!.FindFirstVisual<LinearShockSensorConfigurationView>());
    }

    [AvaloniaFact]
    public async Task SetupEditorView_ChangingForkSensorType_ReplacesRenderedForkSensorView()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;
        var editor = mounted.Editor;

        var forkContent = view.FindControl<ContentControl>("ForkSensorConfigContent");
        Assert.NotNull(forkContent);
        Assert.NotNull(forkContent!.FindFirstVisual<LinearForkSensorConfigurationView>());

        editor.ForkSensorType = SensorType.RotationalFork;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.IsType<RotationalForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration);
        Assert.NotNull(forkContent.FindFirstVisual<RotationalForkSensorConfigurationView>());
    }

    [AvaloniaFact]
    public async Task SetupEditorView_LoadedBehavior_SelectsSensorTypeComboBoxValues()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;

        var forkSensorTypeComboBox = view.FindControl<ComboBox>("ForkSensorTypeComboBox");
        var shockSensorTypeComboBox = view.FindControl<ComboBox>("ShockSensorTypeComboBox");
        Assert.NotNull(forkSensorTypeComboBox);
        Assert.NotNull(shockSensorTypeComboBox);

        Assert.Equal(SensorType.LinearFork, Assert.IsType<SensorType>(forkSensorTypeComboBox!.SelectedItem));
        Assert.Equal(SensorType.LinearShock, Assert.IsType<SensorType>(shockSensorTypeComboBox!.SelectedItem));
    }

    [AvaloniaFact]
    public async Task SetupEditorView_ForkSensorConfigContentControl_Empty_WhenNull()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
        {
            FrontSensorConfigurationJson = null
        });
        var view = mounted.View;
        var editor = mounted.Editor;

        var forkContent = view.FindControl<ContentControl>("ForkSensorConfigContent");
        var forkSensorTypeComboBox = view.FindControl<ComboBox>("ForkSensorTypeComboBox");
        Assert.NotNull(forkContent);
        Assert.NotNull(forkSensorTypeComboBox);

        Assert.Null(editor.ForkSensorConfiguration);
        Assert.Null(forkContent!.Content);
        Assert.Null(forkSensorTypeComboBox!.SelectedItem);
        Assert.Null(forkContent.FindFirstVisual<LinearForkSensorConfigurationView>());
        Assert.Null(forkContent.FindFirstVisual<RotationalForkSensorConfigurationView>());
    }

    [AvaloniaFact]
    public async Task SetupEditorView_ShockSensorConfigContentControl_Empty_WhenNull()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
        {
            RearSensorConfigurationJson = null
        });
        var view = mounted.View;
        var editor = mounted.Editor;

        var shockContent = view.FindControl<ContentControl>("ShockSensorConfigContent");
        var shockSensorTypeComboBox = view.FindControl<ComboBox>("ShockSensorTypeComboBox");
        Assert.NotNull(shockContent);
        Assert.NotNull(shockSensorTypeComboBox);

        Assert.Null(editor.ShockSensorConfiguration);
        Assert.Null(shockContent!.Content);
        Assert.Null(shockSensorTypeComboBox!.SelectedItem);
        Assert.Null(shockContent.FindFirstVisual<LinearShockSensorConfigurationView>());
        Assert.Null(shockContent.FindFirstVisual<RotationalShockSensorConfigurationView>());
    }
}