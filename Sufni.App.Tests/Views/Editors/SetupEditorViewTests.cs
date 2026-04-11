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
    public async Task SetupEditorView_LoadedBehavior_PopulatesBikeSelection_AndRendersSensorViews()
    {
        TestApp.SetIsDesktop(false);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike("Enduro bike");
        var boardId = Guid.NewGuid();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike, boardId));
        var view = new SetupEditorView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var boardIdTextBox = view.FindControl<TextBox>("BoardIdTextBox");
            var bikeComboBox = view.FindControl<ComboBox>("BikeComboBox");
            var forkContent = view.FindControl<ContentControl>("ForkSensorConfigContent");
            var shockContent = view.FindControl<ContentControl>("ShockSensorConfigContent");
            Assert.NotNull(boardIdTextBox);
            Assert.NotNull(bikeComboBox);
            Assert.NotNull(forkContent);
            Assert.NotNull(shockContent);

            Assert.Equal(boardId.ToString(), boardIdTextBox!.Text);
            Assert.Single(editor.Bikes);
            Assert.Equal(bike.Id, editor.SelectedBike?.Id);
            Assert.Equal(bike.Id, Assert.IsType<BikeSnapshot>(bikeComboBox!.SelectedItem).Id);
            Assert.NotNull(view.FindFirstVisual<EditableTitle>());
            Assert.NotNull(view.FindFirstVisual<CommonButtonLine>());
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
    public async Task SetupEditorView_ChangingForkSensorType_ReplacesRenderedForkSensorView()
    {
        TestApp.SetIsDesktop(false);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = new SetupEditorView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var forkContent = view.FindControl<ContentControl>("ForkSensorConfigContent");
            Assert.NotNull(forkContent);
            Assert.NotNull(forkContent!.FindFirstVisual<LinearForkSensorConfigurationView>());

            editor.ForkSensorType = SensorType.RotationalFork;
            await ViewTestHelpers.FlushDispatcherAsync();

            Assert.IsType<RotationalForkSensorConfigurationViewModel>(editor.ForkSensorConfiguration);
            Assert.NotNull(forkContent.FindFirstVisual<RotationalForkSensorConfigurationView>());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorView_LoadedBehavior_SelectsSensorTypeComboBoxValues()
    {
        TestApp.SetIsDesktop(false);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = new SetupEditorView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
            var forkSensorTypeComboBox = view.FindControl<ComboBox>("ForkSensorTypeComboBox");
            var shockSensorTypeComboBox = view.FindControl<ComboBox>("ShockSensorTypeComboBox");
            Assert.NotNull(forkSensorTypeComboBox);
            Assert.NotNull(shockSensorTypeComboBox);

            Assert.Equal(SensorType.LinearFork, Assert.IsType<SensorType>(forkSensorTypeComboBox!.SelectedItem));
            Assert.Equal(SensorType.LinearShock, Assert.IsType<SensorType>(shockSensorTypeComboBox!.SelectedItem));
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }
}