using Sufni.App.Shell.Coordinators;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using NSubstitute;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.Kinematics;
using Sufni.App.Setups.Views.Shared;
using Sufni.App.Tests.Setups.Views.Editors;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Setups.DesktopViews.Editors;

[Collection("Ui")]
public class SetupEditorDesktopViewTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BikeMetadataUpdate_PreservesTheSetupSelection(bool rotationalSensor)
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var setup = SetupEditorViewTestContext.CreateSetupSnapshot(bike);
        if (rotationalSensor)
        {
            setup = setup with
            {
                RearSensorConfigurationJson = SensorConfiguration.ToJson(new RotationalShockSensorConfiguration
                {
                    CentralJoint = JointNames.RearWheel,
                    AdjacentJoint1 = JointNames.BottomBracket,
                    AdjacentJoint2 = JointNames.ShockEye1,
                }),
            };
        }
        await using var mounted = await context.MountDesktopAsync(setup);
        Assert.False(mounted.Editor.IsDirty);
        var rearSensor = mounted.Editor.ShockSensorConfiguration?.CaptureDraft();
        var updated = bike with { Name = "Renamed bike", Updated = bike.Updated + 1 };

        context.UpdateBike(updated);
        await ViewTestHelpers.FlushDispatcherAsync();

        var selector = mounted.View.FindFirstVisual<SetupEditorCommonFields>()!.FindControl<ComboBox>("BikeComboBox")!;
        Assert.Equal(updated, Assert.IsType<BikeSnapshot>(selector.SelectedItem));
        Assert.Equal(updated, mounted.Editor.SelectedBike);
        Assert.Equal(rearSensor, mounted.Editor.ShockSensorConfiguration?.CaptureDraft());
        Assert.False(mounted.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task ReattachingView_PreservesAnUnsavedBikeSelection()
    {
        using var context = new SetupEditorViewTestContext();
        var original = context.AddBike();
        var selected = context.AddBike("Chosen bike");
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(original));
        var selector = mounted.View.FindFirstVisual<SetupEditorCommonFields>()!.FindControl<ComboBox>("BikeComboBox")!;
        selector.SelectedItem = selected;
        await ViewTestHelpers.FlushDispatcherAsync();

        mounted.Host.Content = null;
        await ViewTestHelpers.FlushDispatcherAsync();
        mounted.Host.Content = mounted.View;
        await ViewTestHelpers.FlushDispatcherAsync();

        Assert.Equal(selected.Id, Assert.IsType<BikeSnapshot>(selector.SelectedItem).Id);
        Assert.Equal(selected.Id, mounted.Editor.SelectedBike?.Id);
        Assert.True(mounted.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_NameTextBox_DisplaysBoundValue()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
        {
            Name = "Race setup"
        });

        var nameTextBox = mounted.View.FindControl<TextBox>("NameTextBox");
        Assert.NotNull(nameTextBox);
        Assert.Equal("Race setup", nameTextBox!.Text);
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_SaveButton_BindsSaveCommand()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        var saveButton = mounted.View.FindControl<Button>("SaveButton");
        Assert.NotNull(saveButton);
        Assert.Same(mounted.Editor.SaveCommand, saveButton!.Command);
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_ResetButton_BindsResetCommand()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));

        var resetButton = mounted.View.FindControl<Button>("ResetButton");
        Assert.NotNull(resetButton);
        Assert.Same(mounted.Editor.ResetCommand, resetButton!.Command);
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_BikeComboBox_SelectedItemPresenter_HasEditButton()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountDesktopAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;
        var editor = mounted.Editor;

        var commonFields = view.FindFirstVisual<SetupEditorCommonFields>();
        Assert.NotNull(commonFields);

        var bikeComboBox = commonFields!.FindControl<ComboBox>("BikeComboBox");
        Assert.NotNull(bikeComboBox);

        var selectedItemHost = AttachSelectedBikeTemplateHost(commonFields, bikeComboBox!, bike);
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

        var commonFields = view.FindFirstVisual<SetupEditorCommonFields>();
        Assert.NotNull(commonFields);

        var bikeComboBox = commonFields!.FindControl<ComboBox>("BikeComboBox");
        Assert.NotNull(bikeComboBox);

        var selectedItemHost = AttachSelectedBikeTemplateHost(commonFields, bikeComboBox!, bike);
        await ViewTestHelpers.FlushDispatcherAsync();

        var editBikeButton = Assert.Single(selectedItemHost.GetLogicalDescendants().OfType<Button>());

        editBikeButton.Command!.Execute(editBikeButton.CommandParameter);
        await ViewTestHelpers.FlushDispatcherAsync();

        context.Shell.Received(1).OpenEditor(new EditorKey.Bike(bike.Id));
    }

    private static ContentControl AttachSelectedBikeTemplateHost(
        SetupEditorCommonFields commonFields,
        ComboBox bikeComboBox,
        BikeSnapshot bike)
    {
        // Headless tests do not realize the ComboBox selected-item presenter button, so render the real item template in-tree.
        var rootGrid = commonFields.FindFirstVisual<Grid>();
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
