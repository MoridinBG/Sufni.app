using System.Linq;
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using NSubstitute;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.Controls;
using Sufni.App.Views.SensorConfigurations;

namespace Sufni.App.Tests.Views.Editors;

public class SetupEditorDesktopViewTests
{
    [AvaloniaFact]
    public async Task SetupEditorDesktopView_LoadedBehavior_PopulatesFields_AndRendersSensorViews()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike("Trail bike");
        var boardId = Guid.NewGuid();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike, boardId) with
        {
            Name = "Race setup"
        });
        var view = mounted.View;
        var editor = mounted.Editor;

        var nameTextBox = view.FindControl<TextBox>("NameTextBox");
        var boardIdTextBox = view.FindControl<TextBox>("BoardIdTextBox");
        var bikeComboBox = view.FindControl<ComboBox>("BikeComboBox");
        var forkContent = view.FindControl<ContentControl>("ForkSensorConfigContent");
        var shockContent = view.FindControl<ContentControl>("ShockSensorConfigContent");
        Assert.NotNull(nameTextBox);
        Assert.NotNull(boardIdTextBox);
        Assert.NotNull(bikeComboBox);
        Assert.NotNull(forkContent);
        Assert.NotNull(shockContent);

        Assert.Equal("Race setup", nameTextBox!.Text);
        Assert.Equal(boardId.ToString(), boardIdTextBox!.Text);
        Assert.Single(editor.Bikes);
        Assert.Equal(bike.Id, editor.SelectedBike?.Id);
        Assert.Equal(bike.Id, Assert.IsType<BikeSnapshot>(bikeComboBox!.SelectedItem).Id);
        Assert.NotNull(forkContent!.FindFirstVisual<LinearForkSensorConfigurationView>());
        Assert.NotNull(shockContent!.FindFirstVisual<LinearShockSensorConfigurationView>());
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_BindsSaveAndResetButtons_ToViewModelCommands()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;
        var editor = mounted.Editor;

        var saveButton = view.FindControl<Button>("SaveButton");
        var resetButton = view.FindControl<Button>("ResetButton");
        Assert.NotNull(saveButton);
        Assert.NotNull(resetButton);

        Assert.Same(editor.SaveCommand, saveButton!.Command);
        Assert.Same(editor.ResetCommand, resetButton!.Command);
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_ForkSensorConfigContentControl_Empty_WhenNull()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
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
    public async Task SetupEditorDesktopView_ShockSensorConfigContentControl_Empty_WhenNull()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
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

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_DoesNotUseEditableTitle()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        Assert.Null(mounted.View.FindFirstVisual<EditableTitle>());
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_DoesNotUseCommonButtonLine()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        Assert.Null(mounted.View.FindFirstVisual<CommonButtonLine>());
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_BikeComboBox_SelectedItemPresenter_HasEditButton()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;
        var editor = mounted.Editor;

        var bikeComboBox = view.FindControl<ComboBox>("BikeComboBox");
        Assert.NotNull(bikeComboBox);

        var selectedItemHost = AttachSelectedBikeTemplateHost(view, bikeComboBox!, bike);
        await ViewTestHelpers.FlushDispatcherAsync();

        var editBikeButton = Assert.Single(selectedItemHost.GetLogicalDescendants().OfType<Button>());

        Assert.Same(editor.EditBikeCommand, editBikeButton.Command);
        Assert.Equal(bike.Id, Assert.IsType<BikeSnapshot>(editBikeButton.CommandParameter).Id);
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_EditBikeButton_InvokesEditBikeCommand()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;

        var bikeComboBox = view.FindControl<ComboBox>("BikeComboBox");
        Assert.NotNull(bikeComboBox);

        var selectedItemHost = AttachSelectedBikeTemplateHost(view, bikeComboBox!, bike);
        await ViewTestHelpers.FlushDispatcherAsync();

        var editBikeButton = Assert.Single(selectedItemHost.GetLogicalDescendants().OfType<Button>());

        editBikeButton.Command!.Execute(editBikeButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        await context.BikeCoordinator.Received(1).OpenEditAsync(bike.Id);
    }

    private static ContentControl AttachSelectedBikeTemplateHost(SetupEditorDesktopView view, ComboBox bikeComboBox, BikeSnapshot bike)
    {
        // Headless tests do not realize the ComboBox selected-item presenter button, so render the real item template in-tree.
        var rootGrid = view.FindFirstVisual<Grid>();
        Assert.NotNull(rootGrid);

        var template = Assert.Single(bikeComboBox.DataTemplates);
        var selectedItemHost = new ContentControl
        {
            Content = bike,
            ContentTemplate = template
        };

        rootGrid!.Children.Add(selectedItemHost);
        return selectedItemHost;
    }
}