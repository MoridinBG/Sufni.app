using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Labs.Controls;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using NSubstitute;
using Sufni.App.ExtensionHost.Contracts.RecordedSessions;
using Sufni.App.ExtensionHost.Contracts.Capabilities;
using Sufni.App.ExtensionHost.Runtime.RecordedSessions;
using Sufni.App.Extensibility.Views;
using Sufni.App.Tests.TestSupport;

namespace Sufni.App.Tests.Views.SessionPages;

[Collection("Ui")]
public class ZZDiagToolbarTests
{
    [AvaloniaFact]
    public async Task Diag()
    {
        ViewTestHelpers.EnsureViewTestResources();

        var slots = new RecordedSessionExtensionSlots();
        var command = new RelayCommand(() => { });
        slots.GraphToolbarCommands.Add(new RecordedSessionToolbarCommandContribution(
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

        var sb = new StringBuilder();
        var leadingBar = view.FindControl<CommandBar>("LeadingGraphToolbarCommandBar");
        sb.AppendLine("ExtensionSlots null? " + (view.ExtensionSlots is null));
        sb.AppendLine("LeadingBar null? " + (leadingBar is null));
        sb.AppendLine("PrimaryCommands.Count = " + (leadingBar?.PrimaryCommands.Count ?? -1));
        sb.AppendLine("PrimaryCommands types: " + (leadingBar is null ? "n/a" : string.Join(",", leadingBar.PrimaryCommands.Select(c => c.GetType().Name))));
        var buttons = view.GetVisualDescendants().OfType<CommandBarButton>().ToArray();
        sb.AppendLine("Visual CommandBarButton count = " + buttons.Length);
        sb.AppendLine("Visual CommandBarButton labels = " + string.Join(",", buttons.Select(b => b.Label)));

        host.Close();
        throw new Exception("DIAG\n" + sb);
    }
}
