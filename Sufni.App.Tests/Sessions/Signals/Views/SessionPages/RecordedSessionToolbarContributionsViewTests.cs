using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Extensibility.Views;
using Sufni.App.Tests.TestSupport.Harness;

namespace Sufni.App.Tests.Sessions.Signals.Views.SessionPages;

[Collection("Ui")]
public class RecordedSessionToolbarContributionsViewTests
{
    [AvaloniaFact]
    public async Task RecordedSessionToolbarContributionsView_AddsLeadingCommands()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var slots = new RecordedSessionExtensionSlots();
        var command = new RelayCommand(() => { });
        slots.SignalToolbarCommands.Add(new RecordedSessionToolbarCommandContribution(
            "extension",
            "toolbar-command",
            Order: 0,
            RecordedSessionToolbarZone.Leading,
            "Match",
            new ToolbarIconDescriptor("/Assets/fa-link.svg", Width: 17, Height: 19),
            command));

        var view = new RecordedSessionToolbarContributionsView
        {
            ExtensionSlots = slots,
        };

        var host = await ViewTestHelpers.ShowViewAsync(view);
        try
        {
            var leadingBar = view.FindControl<CommandBar>("LeadingSignalToolbarCommandBar");
            Assert.NotNull(leadingBar);

            var button = Assert.Single(leadingBar!.PrimaryCommands.OfType<CommandBarButton>());
            Assert.Equal("Match", button.Label);
            Assert.Same(command, button.Command);
        }
        finally
        {
            host.Close();
        }
    }
}
