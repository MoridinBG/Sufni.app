using CommunityToolkit.Mvvm.ComponentModel;

using Sufni.App.LiveDaq.Stores;
namespace Sufni.App.LiveDaq.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="LiveDaqSnapshot"/> for use
/// in the desktop live DAQ list. Unlike the persisted-entity row types,
/// this row does not implement any shared delete-oriented list contract.
/// </summary>
public partial class LiveDaqRowViewModel : ObservableObject
{
    public string IdentityKey { get; private set; } = string.Empty;

    [ObservableProperty] public partial string DisplayName { get; set; } = string.Empty;
    [ObservableProperty] public partial string? BoardId { get; set; }
    [ObservableProperty] public partial string? Endpoint { get; set; }
    [ObservableProperty] public partial bool IsOnline { get; set; }
    [ObservableProperty] public partial string? SetupName { get; set; }
    [ObservableProperty] public partial string? BikeName { get; set; }

    // Show BoardId only when it differs from the display name.
    [ObservableProperty] public partial bool ShowBoardId { get; set; }

    // Show Endpoint only when it differs from the display name.
    [ObservableProperty] public partial bool ShowEndpoint { get; set; }

    public LiveDaqRowViewModel(LiveDaqSnapshot snapshot)
    {
        Update(snapshot);
    }

    public void Update(LiveDaqSnapshot snapshot)
    {
        IdentityKey = snapshot.IdentityKey;
        DisplayName = snapshot.DisplayName;
        BoardId = snapshot.BoardId;
        Endpoint = snapshot.Endpoint;
        IsOnline = snapshot.IsOnline;
        SetupName = snapshot.SetupName;
        BikeName = snapshot.BikeName;
        ShowBoardId = snapshot.BoardId is not null &&
                      !string.Equals(snapshot.BoardId, snapshot.DisplayName, System.StringComparison.OrdinalIgnoreCase);
        ShowEndpoint = snapshot.Endpoint is not null &&
                       !string.Equals(snapshot.Endpoint, snapshot.DisplayName, System.StringComparison.OrdinalIgnoreCase);
    }
}