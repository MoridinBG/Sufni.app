using System.Windows.Input;
using ScottPlot;
using Sufni.App.Models;
using Sufni.App.ViewModels.Editors;
using Sufni.App.Views.Plots;
using Sufni.Telemetry;

namespace Sufni.App.Tests.Views.Plots;

public class TelemetryPlotContextMenuTests
{
    [Fact]
    public void ShowContextMenu_AppendsExecutableWorkspaceActionsAfterDefaults()
    {
        var context = new TelemetryPlotContextMenuContext(
            TelemetryGraphRowIds.Travel,
            ClickSeconds: 2,
            DurationSeconds: 10,
            AnalysisRange: new TelemetryTimeRange(1, 3));
        var command = new TrackingCommand(canExecute: true);
        var fakeMenu = new FakePlotMenu();
        var menu = CreateMenu(
            fakeMenu,
            _ => context,
            _ => [new TelemetryPlotContextMenuAction("autozoom", "Autozoom", command)]);

        menu.ShowContextMenu(new Pixel(10, 20));

        Assert.Equal("Default", fakeMenu.ContextMenuItems[0].Label);
        Assert.True(fakeMenu.ContextMenuItems[1].IsSeparator);
        Assert.Equal("Autozoom", fakeMenu.ContextMenuItems[2].Label);
        Assert.Equal(new Pixel(10, 20), fakeMenu.LastShowPixel);

        fakeMenu.ContextMenuItems[2].OnInvoke(new Plot());

        Assert.Same(context, command.LastExecutedParameter);
    }

    [Fact]
    public void ShowContextMenu_WhenContextIsMissing_ShowsOnlyDefaultItems()
    {
        var fakeMenu = new FakePlotMenu();
        var menu = CreateMenu(
            fakeMenu,
            _ => null,
            _ => throw new InvalidOperationException("Actions should not be resolved without context."));

        menu.ShowContextMenu(new Pixel(1, 2));

        var item = Assert.Single(fakeMenu.ContextMenuItems);
        Assert.Equal("Default", item.Label);
        Assert.Equal(new Pixel(1, 2), fakeMenu.LastShowPixel);
    }

    [Fact]
    public void ShowContextMenu_OmitsNonExecutableAndBlankLabelActions()
    {
        var context = new TelemetryPlotContextMenuContext(
            TelemetryGraphRowIds.Travel,
            ClickSeconds: 2,
            DurationSeconds: 10,
            AnalysisRange: null);
        var fakeMenu = new FakePlotMenu();
        var menu = CreateMenu(
            fakeMenu,
            _ => context,
            _ =>
            [
                new TelemetryPlotContextMenuAction("disabled", "Disabled", new TrackingCommand(canExecute: false)),
                new TelemetryPlotContextMenuAction("blank", "", new TrackingCommand(canExecute: true)),
            ]);

        menu.ShowContextMenu(new Pixel(1, 2));

        var item = Assert.Single(fakeMenu.ContextMenuItems);
        Assert.Equal("Default", item.Label);
    }

    [Fact]
    public void Reset_RestoresDefaultItemsAfterCustomItemsWereAdded()
    {
        var fakeMenu = new FakePlotMenu();
        var menu = CreateMenu(fakeMenu, _ => null, _ => []);

        menu.Add("Custom", _ => { });
        Assert.Equal(["Default", "Custom"], menu.ContextMenuItems.Select(item => item.Label).ToArray());

        menu.Reset();

        var item = Assert.Single(menu.ContextMenuItems);
        Assert.Equal("Default", item.Label);
    }

    private static TelemetryPlotContextMenu CreateMenu(
        IPlotMenu innerMenu,
        Func<Pixel, TelemetryPlotContextMenuContext?> createContext,
        Func<TelemetryPlotContextMenuContext, IReadOnlyList<TelemetryPlotContextMenuAction>> getActions)
    {
        return new TelemetryPlotContextMenu(
            innerMenu,
            [new ContextMenuItem { Label = "Default", OnInvoke = _ => { } }],
            createContext,
            getActions);
    }

    private sealed class FakePlotMenu : IPlotMenu
    {
        public List<ContextMenuItem> ContextMenuItems { get; set; } = [];
        public Pixel? LastShowPixel { get; private set; }

        public void Reset()
        {
            ContextMenuItems.Clear();
        }

        public void Clear()
        {
            ContextMenuItems.Clear();
        }

        public void Add(string Label, Action<Plot> action)
        {
            ContextMenuItems.Add(new ContextMenuItem
            {
                Label = Label,
                OnInvoke = action
            });
        }

        public void AddSeparator()
        {
            ContextMenuItems.Add(new ContextMenuItem
            {
                IsSeparator = true
            });
        }

        public void ShowContextMenu(Pixel pixel)
        {
            LastShowPixel = pixel;
        }
    }

    private sealed class TrackingCommand(bool canExecute) : ICommand
    {
        public object? LastExecutedParameter { get; private set; }
        public event EventHandler? CanExecuteChanged;

        public bool CanExecute(object? parameter) => canExecute;

        public void Execute(object? parameter)
        {
            LastExecutedParameter = parameter;
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
