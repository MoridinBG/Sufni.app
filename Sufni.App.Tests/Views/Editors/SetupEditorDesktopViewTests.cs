using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Sufni.App.DesktopViews.Editors;
using Sufni.App.Stores;
using Sufni.App.Tests.Infrastructure;
using Sufni.App.Views.SensorConfigurations;

namespace Sufni.App.Tests.Views.Editors;

public class SetupEditorDesktopViewTests
{
    [AvaloniaFact]
    public async Task SetupEditorDesktopView_LoadedBehavior_PopulatesFields_AndRendersSensorViews()
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike("Trail bike");
        var boardId = Guid.NewGuid();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike, boardId) with
        {
            Name = "Race setup"
        });
        var view = new SetupEditorDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
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
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_BindsSaveAndResetButtons_ToViewModelCommands()
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = new SetupEditorDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var saveButton = view.FindControl<Button>("SaveButton");
            var resetButton = view.FindControl<Button>("ResetButton");
            Assert.NotNull(saveButton);
            Assert.NotNull(resetButton);

            Assert.Same(editor.SaveCommand, saveButton!.Command);
            Assert.Same(editor.ResetCommand, resetButton!.Command);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}