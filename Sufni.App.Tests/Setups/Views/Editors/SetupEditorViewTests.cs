using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Sufni.App.Bikes.Stores;
using Sufni.App.Setups.Models.SensorConfigurations;
using Sufni.App.Setups.ViewModels.SensorConfigurations;
using Sufni.App.Setups.Views.SensorConfigurations;
using Sufni.App.Setups.Views.Shared;
using Sufni.App.Tests.TestSupport.Harness;
namespace Sufni.App.Tests.Setups.Views.Editors;

[Collection("Ui")]
public class SetupEditorViewTests
{
    [AvaloniaFact]
    public async Task OpeningSetup_SelectsItsStoredBike()
    {
        using var context = new SetupEditorViewTestContext();
        context.AddBike("Another bike");
        var bike = context.AddBike("Linked bike");
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var selector = mounted.View.FindFirstVisual<SetupEditorCommonFields>()!.FindControl<ComboBox>("BikeComboBox")!;

        Assert.Equal(bike.Id, Assert.IsType<BikeSnapshot>(selector.SelectedItem).Id);
        Assert.Equal(bike.Id, mounted.Editor.SelectedBike?.Id);
        Assert.False(mounted.Editor.IsDirty);
    }

    [AvaloniaFact]
    public async Task SetupEditorView_ChangingForkSensorType_ReplacesRenderedForkSensorView()
    {
        using var context = new SetupEditorViewTestContext();
        var bike = context.AddBike();
        await using var mounted = await context.MountMobileAsync(SetupEditorViewTestContext.CreateSetupSnapshot(bike));
        var view = mounted.View;
        var editor = mounted.Editor;

        var commonFields = view.FindFirstVisual<SetupEditorCommonFields>();
        Assert.NotNull(commonFields);

        var forkContent = commonFields!.FindControl<ContentControl>("ForkSensorConfigContent");
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

        await ViewTestHelpers.FlushDispatcherAsync();

        var commonFields = view.FindFirstVisual<SetupEditorCommonFields>();
        Assert.NotNull(commonFields);

        var forkSensorTypeComboBox = commonFields!.FindControl<ComboBox>("ForkSensorTypeComboBox");
        var shockSensorTypeComboBox = commonFields.FindControl<ComboBox>("ShockSensorTypeComboBox");
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

        var commonFields = view.FindFirstVisual<SetupEditorCommonFields>();
        Assert.NotNull(commonFields);

        var forkContent = commonFields!.FindControl<ContentControl>("ForkSensorConfigContent");
        var forkSensorTypeComboBox = commonFields.FindControl<ComboBox>("ForkSensorTypeComboBox");
        Assert.NotNull(forkContent);
        Assert.NotNull(forkSensorTypeComboBox);

        Assert.Null(editor.ForkSensorConfiguration);
        Assert.Null(forkContent!.Content);
        Assert.Null(forkSensorTypeComboBox!.SelectedItem);
        Assert.Null(forkContent.FindFirstVisual<LinearForkSensorConfigurationView>());
        Assert.Null(forkContent.FindFirstVisual<RotationalForkSensorConfigurationView>());
    }

}
