using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using NSubstitute;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;

namespace Sufni.App.Tests.Views.Editors;

public class SetupEditorDesktopViewTests
{
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
