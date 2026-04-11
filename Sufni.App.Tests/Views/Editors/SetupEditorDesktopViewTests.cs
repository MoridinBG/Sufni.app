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

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_ForkSensorConfigContentControl_Empty_WhenNull()
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
        {
            FrontSensorConfigurationJson = null
        });
        var view = new SetupEditorDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
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
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_ShockSensorConfigContentControl_Empty_WhenNull()
    {
        TestApp.SetIsDesktop(true);
        ViewTestHelpers.EnsureViewTestResources();
        ViewTestHelpers.EnsureViewTestDataTemplates();

        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        var editor = context.CreateEditor(SetupEditorViewTestContext.CreateSetupSnapshot(bike) with
        {
            RearSensorConfigurationJson = null
        });
        var view = new SetupEditorDesktopView
        {
            DataContext = editor
        };

        var host = ViewTestHelpers.ShowView(view);
        await ViewTestHelpers.FlushDispatcherAsync();

        try
        {
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
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_DoesNotUseEditableTitle()
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
            Assert.Null(view.FindFirstVisual<EditableTitle>());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_DoesNotUseCommonButtonLine()
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
            Assert.Null(view.FindFirstVisual<CommonButtonLine>());
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_BikeComboBox_SelectedItemPresenter_HasEditButton()
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
            var bikeComboBox = view.FindControl<ComboBox>("BikeComboBox");
            Assert.NotNull(bikeComboBox);

            var selectedItemHost = AttachSelectedBikeTemplateHost(view, bikeComboBox!, bike);
            await ViewTestHelpers.FlushDispatcherAsync();

            var editBikeButton = Assert.Single(selectedItemHost.GetLogicalDescendants().OfType<Button>());

            Assert.Same(editor.EditBikeCommand, editBikeButton.Command);
            Assert.Equal(bike.Id, Assert.IsType<BikeSnapshot>(editBikeButton.CommandParameter).Id);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    [AvaloniaFact]
    public async Task SetupEditorDesktopView_EditBikeButton_InvokesEditBikeCommand()
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
            var bikeComboBox = view.FindControl<ComboBox>("BikeComboBox");
            Assert.NotNull(bikeComboBox);

            var selectedItemHost = AttachSelectedBikeTemplateHost(view, bikeComboBox!, bike);
            await ViewTestHelpers.FlushDispatcherAsync();

            var editBikeButton = Assert.Single(selectedItemHost.GetLogicalDescendants().OfType<Button>());

            editBikeButton.Command!.Execute(editBikeButton.CommandParameter);
            await ViewTestHelpers.FlushDispatcherAsync();

            await context.BikeCoordinator.Received(1).OpenEditAsync(bike.Id);
        }
        finally
        {
            host.Close();
            await ViewTestHelpers.FlushDispatcherAsync();
        }
    }

    private static ContentControl AttachSelectedBikeTemplateHost(SetupEditorDesktopView view, ComboBox bikeComboBox, BikeSnapshot bike)
    {
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