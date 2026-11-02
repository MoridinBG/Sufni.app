using System.Reactive.Subjects;
using NSubstitute;
using Sufni.App.Acquisition.Models;
using Sufni.App.Acquisition.Services;
using Sufni.App.Acquisition.Services.Management;
using Sufni.App.Infrastructure;
using Sufni.App.LiveDaq.Services;
using Sufni.App.LiveDaq.Services.LiveStreaming;

namespace Sufni.App.Tests.TestSupport.Acquisition;

internal sealed class NetworkTelemetrySourceHarness
{
    public Guid BoardId { get; } = Guid.NewGuid();
    public ServiceEndpoint Endpoint { get; } = new("127.0.0.1", 5555);
    public BehaviorSubject<IReadOnlyList<LiveDaqCatalogEntry>> Entries { get; }
    public ILiveDaqCatalogService Catalog { get; } = Substitute.For<ILiveDaqCatalogService>();
    public ILiveDaqSharedStreamRegistry Registry { get; } = Substitute.For<ILiveDaqSharedStreamRegistry>();
    public IDisposable BrowseLease { get; } = Substitute.For<IDisposable>();
    public IDaqManagementService Management { get; }
    public ILiveDaqBoardIdInspector Inspector { get; }

    public NetworkTelemetrySourceHarness(IDaqManagementService? management = null, ILiveDaqBoardIdInspector? inspector = null)
    {
        Management = management ?? Substitute.For<IDaqManagementService>();
        Inspector = inspector ?? Substitute.For<ILiveDaqBoardIdInspector>();
        Entries = new([new("daq", "DAQ", BoardId.ToString(), Endpoint)]);
        Catalog.Observe().Returns(Entries);
        Catalog.Get(Arg.Any<string>()).Returns(call => Entries.Value.FirstOrDefault(entry => entry.IdentityKey == call.Arg<string>()));
        Catalog.AcquireBrowse().Returns(BrowseLease);
        Registry.AcquireImportAsync(Arg.Any<string>(), Arg.Any<Guid>())
            .Returns(_ => Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Acquired(Substitute.For<IDisposable>())));
        Registry.AcquireIdentifyAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Acquired(Substitute.For<IDisposable>())));
        Registry.AcquireManagementAsync(Arg.Any<string>(), Arg.Any<DaqManagementOperation>())
            .Returns(_ => Task.FromResult<DaqTransportAcquisition>(new DaqTransportAcquisition.Acquired(Substitute.For<IDisposable>())));
        if (inspector is null)
            Inspector.InspectAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<BoardIdInspection>(new BoardIdInspection.Found(BoardId)));
        if (management is null) SetDirectory();
    }

    public void SetDirectory(params DaqFileRecord[] files) =>
        Management.ListDirectoryAsync(Arg.Any<ServiceEndpoint>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<DaqListDirectoryResult>(new DaqListDirectoryResult.Listed(new DaqRootDirectoryRecord(files))));

    public NetworkTelemetryDataStore CreateStore() =>
        new(Entries.Value.Single(), Catalog, Registry, Management, Inspector);
}
