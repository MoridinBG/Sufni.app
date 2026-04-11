using CommunityToolkit.Mvvm.ComponentModel;
using Sufni.App.Stores;

namespace Sufni.App.ViewModels.Rows;

/// <summary>
/// Presentation wrapper around a <see cref="LiveDaqSnapshot"/> for use
/// in the desktop live DAQ list. Unlike the persisted-entity row types,
/// this row does not implement any shared delete-oriented list contract.
/// </summary>
public partial class LiveDaqRowViewModel : ObservableObject
{
    public string IdentityKey { get; private set; } = string.Empty;

    [ObservableProperty] private string displayName = string.Empty;
    [ObservableProperty] private string? boardId;
    [ObservableProperty] private string? endpoint;
    [ObservableProperty] private bool isOnline;
    [ObservableProperty] private string? setupName;
    [ObservableProperty] private string? bikeName;

    public LiveDaqRowViewModel() { }

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
    }
}